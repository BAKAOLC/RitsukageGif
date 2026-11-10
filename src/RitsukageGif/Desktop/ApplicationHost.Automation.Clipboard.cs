using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ImageMagick;

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost : IClipboardAutomationService
    {
        async Task<bool> IClipboardAutomationService.IsAvailableAsync(CancellationToken token)
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                RequireClipboardAutomation(token);
                return _services.Context.Clipboard is not null;
            });
        }

        Task<string?> IClipboardAutomationService.ReadTextAsync(CancellationToken token)
        {
            return WithClipboardAsync(async clipboard =>
            {
                using var data = await clipboard.TryGetDataAsync();
                return data is null ? null : await data.TryGetTextAsync();
            }, token);
        }

        Task<string?> IClipboardAutomationService.ReadHtmlAsync(CancellationToken token)
        {
            return WithClipboardAsync(async clipboard =>
            {
                using var data = await clipboard.TryGetDataAsync();
                if (data is null) return null;
                foreach (var name in ClipboardPlatformFormats.HtmlNames)
                foreach (var item in data.Items)
                {
                    token.ThrowIfCancellationRequested();
                    var format = item.Formats.FirstOrDefault(value =>
                        value == DataFormat.CreateBytesPlatformFormat(name));
                    if (format is null) continue;
                    var raw = await item.TryGetRawAsync(format);
                    switch (raw)
                    {
                        case byte[] windows when name == "HTML Format":
                            var parsed = PinnedHtmlFragment.ReadWindows(windows);
                            return parsed.Html.Substring(parsed.Selection.Start, parsed.Selection.Length);
                        case byte[] bytes:
                            return Encoding.UTF8.GetString(bytes).TrimEnd('\0');
                        case string html:
                            return html;
                    }
                }

                return null;
            }, token);
        }

        Task<IReadOnlyList<string>> IClipboardAutomationService.ReadFilesAsync(CancellationToken token)
        {
            return WithClipboardAsync<IReadOnlyList<string>>(async clipboard =>
            {
                using var data = await clipboard.TryGetDataAsync();
                if (data is null) return [];
                var files = await data.TryGetFilesAsync();
                return files?.Select(file => file.TryGetLocalPath()).OfType<string>().ToArray() ?? [];
            }, token);
        }

        Task<byte[]?> IClipboardAutomationService.ReadImageAsync(CancellationToken token)
        {
            return WithClipboardAsync(async clipboard =>
            {
                using var data = await clipboard.TryGetDataAsync();
                if (data is null) return null;
                foreach (var name in ClipboardPlatformFormats.ImageNames)
                foreach (var item in data.Items)
                {
                    var format = DataFormat.CreateBytesPlatformFormat(name);
                    if (!item.Formats.Contains(format)) continue;
                    token.ThrowIfCancellationRequested();
                    if (await item.TryGetRawAsync(format) is not byte[] { Length: > 0 } bytes) continue;
                    return await Task.Run(() =>
                    {
                        if (bytes.Length > PinnedImageContent.MaximumEncodedBytes)
                            throw new InvalidDataException("Clipboard image exceeds the supported size.");
                        var info = new MagickImageInfo(bytes);
                        if ((long)info.Width * info.Height > 128L * 1024 * 1024)
                            throw new InvalidDataException(UiText.Get("InvalidFrameBuffer"));
                        if (info.Format == MagickFormat.Png) return bytes;
                        using var image = new MagickImage(bytes);
                        return image.ToByteArray(MagickFormat.Png);
                    }, token);
                }

                return (await PinnedClipboardSnapshot.ReadBitmapAsync(data, token))?.Png;
            }, token);
        }

        Task IClipboardAutomationService.WriteTextAsync(string text, CancellationToken token)
        {
            return WithClipboardAsync(async clipboard =>
            {
                await clipboard.SetTextAsync(text);
                await clipboard.FlushAsync();
                return true;
            }, token);
        }

        Task IClipboardAutomationService.WriteHtmlAsync(string html, string? text, CancellationToken token)
        {
            return WithClipboardAsync(async clipboard =>
            {
                var data = new DataTransfer();
                var item = new DataTransferItem();
                ClipboardPlatformFormats.SetHtml(item, html);
                if (OperatingSystem.IsWindows())
                    item.Set(DataFormat.CreateBytesPlatformFormat("HTML Format"), ClipboardHtml.CreateWindows(html));
                if (text is not null) item.Set(DataFormat.Text, text);
                data.Add(item);
                token.ThrowIfCancellationRequested();
                await clipboard.SetDataAsync(data);
                await clipboard.FlushAsync();
                return true;
            }, token);
        }

        Task IClipboardAutomationService.WriteFilesAsync(IReadOnlyList<string> paths, CancellationToken token)
        {
            return WithClipboardAsync(async clipboard =>
            {
                var data = new DataTransfer();
                foreach (var path in paths)
                {
                    token.ThrowIfCancellationRequested();
                    var absolute = Path.GetFullPath(path);
                    if (!File.Exists(absolute) && !Directory.Exists(absolute))
                        throw new FileNotFoundException("Clipboard file does not exist.", absolute);
                    data.Add(DataTransferItem.CreateFile(new ClipboardFileReference(absolute,
                        _services.Context.StorageProvider)));
                }

                token.ThrowIfCancellationRequested();
                await clipboard.SetDataAsync(data);
                await clipboard.FlushAsync();
                return true;
            }, token);
        }

        async Task IClipboardAutomationService.WriteImageAsync(ReadOnlyMemory<byte> png, CancellationToken token)
        {
            var bytes = MemoryMarshal.TryGetArray(png, out var segment) &&
                        segment is { Array: { } array, Offset: 0 } && segment.Count == array.Length
                ? array
                : png.ToArray();
            await Task.Run(() =>
            {
                if (bytes.Length > PinnedImageContent.MaximumEncodedBytes ||
                    !bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
                    throw new InvalidDataException("Clipboard image must contain PNG data.");
                var info = new MagickImageInfo(bytes);
                if ((long)info.Width * info.Height > 128L * 1024 * 1024)
                    throw new InvalidDataException(UiText.Get("InvalidFrameBuffer"));
            }, token);
            await WithClipboardAsync(async clipboard =>
            {
                await ClipboardImage.CopyAsync(clipboard, bytes, token);
                return true;
            }, token);
        }

        Task IClipboardAutomationService.ClearAsync(CancellationToken token)
        {
            return WithClipboardAsync(async clipboard =>
            {
                await clipboard.ClearAsync();
                await clipboard.FlushAsync();
                return true;
            }, token);
        }

        private async Task<T> WithClipboardAsync<T>(Func<IClipboard, Task<T>> action, CancellationToken token)
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                RequireClipboardAutomation(token);
                return action(_services.Context.Clipboard ??
                              throw new InvalidOperationException(UiText.Get("ClipboardFailed")));
            });
        }

        private void RequireClipboardAutomation(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (IsClosing) throw new InvalidOperationException(UiText.Get("CommandNotReady"));
        }
    }
}

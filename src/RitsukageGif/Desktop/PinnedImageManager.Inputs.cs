using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ImageMagick;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedImageManager
    {
        private readonly SemaphoreSlim _inputGate = new(2);

        public async Task PinHdrAsync(byte[] sdrPng, CapturedFrame hdr, double captureScaling = 1,
            PixelPoint? position = null, PixelPoint contentOffset = default, CancellationToken token = default)
        {
            if (!double.IsFinite(captureScaling) || captureScaling is < 1 or > 16)
                throw new ArgumentOutOfRangeException(nameof(captureScaling));
            ObjectDisposedException.ThrowIf(_disposed, this);
            await _inputGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                PinLogger.ZLogDebug($"HDR pinned image preparation started.");
                var png = await HdrImageService.EncodePngAsync(hdr, token).ConfigureAwait(false);
                var content = await Task.Run(() =>
                        PinnedImageContent.FromHdr(png, PinnedImageContent.Decode(sdrPng, token), hdr), token)
                    .ConfigureAwait(false);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    token.ThrowIfCancellationRequested();
                    Pin(content, position, "", captureScaling, contentOffset);
                }, DispatcherPriority.Normal, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                PinLogger.ZLogDebug($"HDR pinned image preparation cancelled.");
                throw;
            }
            catch (Exception error)
            {
                PinLogger.ZLogError(error, $"HDR pinned image preparation failed.");
                throw;
            }
            finally
            {
                _inputGate.Release();
            }
        }

        public async Task PinAsync(ReadOnlyMemory<byte> encoded, double captureScaling = 1,
            PixelPoint? position = null, PixelPoint contentOffset = default, CancellationToken token = default)
        {
            if (!double.IsFinite(captureScaling) || captureScaling is < 1 or > 16)
                throw new ArgumentOutOfRangeException(nameof(captureScaling));
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (encoded.IsEmpty || encoded.Length > PinnedImageContent.MaximumEncodedBytes)
                throw new ArgumentOutOfRangeException(nameof(encoded));
            await _inputGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                await DecodeAndPinAsync(encoded, position, "", captureScaling, contentOffset, token)
                    .ConfigureAwait(false);
            }
            finally
            {
                _inputGate.Release();
            }
        }

        private async Task DecodeAndPinAsync(ReadOnlyMemory<byte> encoded, PixelPoint? position,
            string group, double captureScaling = 1, PixelPoint contentOffset = default,
            CancellationToken token = default)
        {
            PinLogger.ZLogDebug($"Pinned image decoding started. EncodedBytes={encoded.Length}");
            try
            {
                var content = await Task.Run(() => PinnedImageContent.Decode(encoded, token), token)
                    .ConfigureAwait(false);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    token.ThrowIfCancellationRequested();
                    Pin(content, position, group, captureScaling, contentOffset);
                }, DispatcherPriority.Normal, token);
                PinLogger.ZLogDebug($"Pinned image decoding completed.");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                PinLogger.ZLogDebug($"Pinned image decoding cancelled.");
                throw;
            }
            catch (Exception error)
            {
                PinLogger.ZLogError(error, $"Pinned image decoding or presentation failed.");
                throw;
            }
        }

        private async Task PinFileAsync(string path, PixelPoint? position = null, string group = "",
            CancellationToken token = default)
        {
            Dispatcher.UIThread.VerifyAccess();
            ObjectDisposedException.ThrowIf(_disposed, this);
            PinLogger.ZLogDebug($"Pinned image file open requested.");
            var file = new FileInfo(Path.GetFullPath(path));
            if (file.Length is <= 0 or > PinnedImageContent.MaximumEncodedBytes)
                throw new ArgumentOutOfRangeException(nameof(path));
            await _inputGate.WaitAsync(token);
            try
            {
                await using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read,
                    81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
                if (stream.Length is <= 0 or > PinnedImageContent.MaximumEncodedBytes)
                    throw new ArgumentOutOfRangeException(nameof(path));
                var encoded = new byte[checked((int)stream.Length)];
                await stream.ReadExactlyAsync(encoded, token);
                await DecodeAndPinAsync(encoded, position, group, token: token);
            }
            finally
            {
                _inputGate.Release();
            }
        }

        public async Task<bool> PinClipboardAsync(IClipboard clipboard, CancellationToken token = default)
        {
            Dispatcher.UIThread.VerifyAccess();
            ObjectDisposedException.ThrowIf(_disposed, this);
            token.ThrowIfCancellationRequested();
            using (var transfer = await ReadClipboardCandidateAsync(clipboard.TryGetDataAsync, token))
                if (transfer is not null)
                    foreach (var (name, animationOnly) in OperatingSystem.IsWindows()
                                 ? new[]
                                 {
                                     ("image/apng", true), ("image/gif", true), ("GIF", true), ("image/webp", true),
                                     ("image/png", false), ("PNG", false), ("image/apng", false),
                                     ("image/gif", false), ("GIF", false), ("image/webp", false),
                                 }
                                 :
                                 [
                                     ("image/apng", true), ("image/gif", true), ("image/webp", true),
                                     ("image/png", false), ("image/apng", false), ("image/gif", false),
                                     ("image/webp", false),
                                 ])
                    {
                        var format = DataFormat.CreateBytesPlatformFormat(name);
                        foreach (var item in transfer.Items)
                        {
                            token.ThrowIfCancellationRequested();
                            if (!item.Formats.Contains(format)) continue;
                            if (await ReadClipboardCandidateAsync(() => item.TryGetRawAsync(format), token)
                                is not byte[] { Length: > 0 } png) continue;
                            var content = await TryPrepareClipboardAsync(
                                () => Task.Run(() => animationOnly && AnimationPinMetadata.Read(png, token) is null
                                    ? null
                                    : PinnedImageContent.Decode(png, token), token), token);
                            if (content is null) continue;
                            token.ThrowIfCancellationRequested();
                            Pin(content, null, "");
                            return true;
                        }
                    }

            using var bitmap = await ReadClipboardCandidateAsync(clipboard.TryGetBitmapAsync, token);
            token.ThrowIfCancellationRequested();
            if (bitmap is not null)
            {
                var content = await TryPrepareClipboardAsync(async () =>
                {
                    if ((long)bitmap.PixelSize.Width * bitmap.PixelSize.Height > 128L * 1024 * 1024)
                        throw new InvalidDataException(UiText.Get("InvalidFrameBuffer"));
                    using var stream = new MemoryStream();
                    await Task.Run(() => bitmap.Save(stream), token);
                    return await Task.Run(() => PinnedImageContent.Decode(stream.ToArray(), token), token);
                }, token);
                if (content is not null)
                {
                    token.ThrowIfCancellationRequested();
                    Pin(content, null, "");
                    return true;
                }
            }

            var files = await ReadClipboardCandidateAsync(clipboard.TryGetFilesAsync, token);
            token.ThrowIfCancellationRequested();
            foreach (var file in files?.OfType<IStorageFile>() ?? [])
            {
                token.ThrowIfCancellationRequested();
                if (file.TryGetLocalPath() is not { } path) continue;
                var content = await TryPrepareClipboardAsync(async () =>
                {
                    var info = new FileInfo(path);
                    if (info.Length is <= 0 or > PinnedImageContent.MaximumEncodedBytes)
                        throw new InvalidDataException(UiText.Get("InvalidFrameBuffer"));
                    await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                        81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    if (stream.Length is <= 0 or > PinnedImageContent.MaximumEncodedBytes)
                        throw new InvalidDataException(UiText.Get("InvalidFrameBuffer"));
                    var encoded = new byte[checked((int)stream.Length)];
                    await stream.ReadExactlyAsync(encoded, token);
                    return await Task.Run(() => PinnedImageContent.Decode(encoded, token), token);
                }, token);
                if (content is null) continue;
                token.ThrowIfCancellationRequested();
                Pin(content, null, "");
                return true;
            }

            return false;
        }

        private async Task<PinnedImageContent?> TryPrepareClipboardAsync(Func<Task<PinnedImageContent?>> prepare,
            CancellationToken token)
        {
            await _inputGate.WaitAsync(token);
            try
            {
                var content = await prepare();
                token.ThrowIfCancellationRequested();
                return content;
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException
                                              or ArgumentException or NotSupportedException or OverflowException or
                                              MagickException)
            {
                token.ThrowIfCancellationRequested();
                PinLogger.ZLogWarning(error,
                    $"Pinned image clipboard candidate could not be decoded; trying another format.");
                return null;
            }
            finally
            {
                _inputGate.Release();
            }
        }

        private static async Task<T?> ReadClipboardCandidateAsync<T>(Func<Task<T?>> read, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var value = await read();
                token.ThrowIfCancellationRequested();
                return value;
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException
                                              or ArgumentException or NotSupportedException or
                                              MagickException)
            {
                token.ThrowIfCancellationRequested();
                PinLogger.ZLogWarning(error,
                    $"Pinned image clipboard format could not be read; trying another format.");
                return default;
            }
        }

        // ReSharper disable once UnusedMethodReturnValue.Global
        public async Task<bool> OpenImageAsync(Window owner, CancellationToken token = default)
        {
            PinLogger.ZLogDebug($"Pinned image file picker requested.");
            var files = await owner.StorageProvider.OpenFilePickerAsync(new()
            {
                Title = UiText.Get("PinnedImageOpen"), AllowMultiple = false,
                FileTypeFilter = [FilePickerFileTypes.ImageAll],
            });
            token.ThrowIfCancellationRequested();
            if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path)
            {
                PinLogger.ZLogDebug($"Pinned image file picker cancelled or returned no local image.");
                return false;
            }

            await PinFileAsync(path, token: token);
            return true;
        }
    }
}

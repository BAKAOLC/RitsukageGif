using System.Text;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedDocumentWindow
    {
        public async Task<ScreenshotResult> CapturePreferredOutputAsync(OutputColorMode mode, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (mode == OutputColorMode.Hdr) throw new NotSupportedException(UiText.Get("HdrCaptureUnavailable"));
            if (Document is PinnedFormulaDocument)
            {
                var data = _formulaPngTask is not null ? await _formulaPngTask.WaitAsync(token) : null;
                token.ThrowIfCancellationRequested();
                if (data is null) throw new InvalidOperationException("The formula cannot be rendered.");
                return new(data, ScreenshotOutputAction.Save) { ColorMode = OutputColorMode.Sdr, CaptureScaling = 1 };
            }

            if (!PreferencesProvider().CopyVisibleRegion && _textView is not null)
                return await CaptureFullTextAsync(token);
            var width = Math.Max(1, (int)Math.Ceiling(_surface.Bounds.Width * RenderScaling));
            var height = Math.Max(1, (int)Math.Ceiling(_surface.Bounds.Height * RenderScaling));
            if ((long)width * height > 32L * 1024 * 1024)
                throw new InvalidOperationException(UiText.Get("PinnedDocumentImageTooLarge"));
            using var bitmap = new RenderTargetBitmap(new(width, height), new(96 * RenderScaling, 96 * RenderScaling));
            bitmap.Render(_surface);
            using var stream = new MemoryStream();
            await Task.Run(() => bitmap.Save(stream, PngBitmapEncoderOptions.Default), token);
            return new(stream.ToArray(), ScreenshotOutputAction.Save)
                { ColorMode = OutputColorMode.Sdr, CaptureScaling = RenderScaling };
        }

        public Task CompleteSelectionSaveAsync(IReadOnlyList<string> paths, ScreenshotSaveProfile profile,
            ScreenshotResult output)
        {
            return ScreenshotSaveCompletion.RunAsync(paths, profile, Clipboard, StorageProvider, CopyRenderedImageAsync,
                null, _lifetime.Token);
        }

        private Task CopyAsync()
        {
            if (_textView is { SelectedText.Length: > 0 } text)
                return text.SelectedHtml is { } selectionHtml
                    ? CopyHtmlAsync(text.SelectedText, selectionHtml,
                        (Document as PinnedHtmlDocument)?.SourceUrl)
                    : CopyTextAsync(text.SelectedText);
            if (Document is PinnedFilesDocument files) return CopyFilesAsync(files.Paths);
            var clipboard = Clipboard ?? throw new InvalidOperationException(UiText.Get("ClipboardFailed"));
            var transfer = new DataTransfer();
            var item = DataTransferItem.CreateText(Document.PlainText);
            var html = Document switch
            {
                PinnedHtmlDocument rich when !IgnoreFormat => rich.Html,
                PinnedColorDocument { Html.Length: > 0 } color => color.Html,
                _ => null,
            };
            if (html is not null)
            {
                var nativeSelection =
                    (Document as PinnedHtmlDocument)?.Selection ?? PinnedHtmlSelection.FromMarkers(html);
                var selectedHtml = nativeSelection?.Extract(html) ?? html;
                ClipboardPlatformFormats.SetHtml(item,
                    PinnedHtmlFileSource.WithBase(selectedHtml, (Document as PinnedHtmlDocument)?.SourceUrl));
                if (OperatingSystem.IsWindows())
                    item.Set(DataFormat.CreateBytesPlatformFormat("HTML Format"), nativeSelection is null
                        ? ClipboardHtml.CreateWindows(html, (Document as PinnedHtmlDocument)?.SourceUrl)
                        : ClipboardHtml.CreateWindowsContext(html, nativeSelection,
                            (Document as PinnedHtmlDocument)?.SourceUrl));
            }

            transfer.Add(item);
            return PublishAsync();

            async Task PublishAsync()
            {
                await clipboard.SetDataAsync(transfer);
                await clipboard.FlushAsync();
            }
        }

        private async Task CopyTextAsync(string text)
        {
            var clipboard = Clipboard ?? throw new InvalidOperationException(UiText.Get("ClipboardFailed"));
            await clipboard.SetTextAsync(text);
            await clipboard.FlushAsync();
        }

        private async Task CopyHtmlAsync(string text, string html, string? sourceUrl)
        {
            var item = DataTransferItem.CreateText(text);
            ClipboardPlatformFormats.SetHtml(item, PinnedHtmlFileSource.WithBase(html, sourceUrl));
            if (OperatingSystem.IsWindows())
                item.Set(DataFormat.CreateBytesPlatformFormat("HTML Format"),
                    ClipboardHtml.CreateWindows(html, sourceUrl));
            var transfer = new DataTransfer();
            transfer.Add(item);
            var clipboard = Clipboard ?? throw new InvalidOperationException(UiText.Get("ClipboardFailed"));
            await clipboard.SetDataAsync(transfer);
            await clipboard.FlushAsync();
        }

        private async Task CopyFilesAsync(IReadOnlyList<string> paths)
        {
            var token = _lifetime.Token;
            var transfer = await CreateFilesTransferAsync(paths);
            token.ThrowIfCancellationRequested();
            var clipboard = Clipboard ?? throw new InvalidOperationException(UiText.Get("ClipboardFailed"));
            await clipboard.SetDataAsync(transfer);
            await clipboard.FlushAsync();
        }

        private async Task<DataTransfer> CreateFilesTransferAsync(IReadOnlyList<string> paths)
        {
            var transfer = new DataTransfer();
            foreach (var path in paths)
            {
                IStorageItem? item = Directory.Exists(path)
                    ? await StorageProvider.TryGetFolderFromPathAsync(path)
                    : await StorageProvider.TryGetFileFromPathAsync(path);
                if (item is null) throw new FileNotFoundException(null, path);
                transfer.Add(DataTransferItem.CreateFile(item));
            }

            return transfer;
        }

        private async Task DragFilesAsync(PointerPressedEventArgs args, IReadOnlyList<string> paths)
        {
            var token = _lifetime.Token;
            var transfer = await CreateFilesTransferAsync(paths);
            token.ThrowIfCancellationRequested();
            args.Pointer.Capture(null);
            using var registration = PinnedDragRegistration.Register(transfer, this, false);
            await DragDrop.DoDragDropAsync(args, transfer, DragDropEffects.Copy);
        }

        private async Task CopyAsFileAsync()
        {
            if (Document is PinnedFilesDocument files)
            {
                await CopyFilesAsync(files.Paths);
                return;
            }

            var token = _lifetime.Token;
            var store = new PinnedDocumentClipboardFiles();
            var content = Document is PinnedHtmlDocument html && !IgnoreFormat
                ? PinnedHtmlFileSource.WithBase(html.Html, html.SourceUrl)
                : Document.PlainText;
            var extension = Document is PinnedHtmlDocument && !IgnoreFormat ? ".html" :
                Document is PinnedFormulaDocument ? ".tex" : ".txt";
            var path = await store.CreateAsync(content, extension, token);
            var published = false;
            try
            {
                token.ThrowIfCancellationRequested();
                await CopyFilesAsync([path]);
                published = true;
                await store.CleanupAsync(new HashSet<string>(OperatingSystem.IsWindows()
                    ? StringComparer.OrdinalIgnoreCase
                    : StringComparer.Ordinal) { path });
            }
            finally
            {
                if (!published) store.Delete(path);
            }
        }

        private async Task SaveDocumentAsync()
        {
            var token = _lifetime.Token;
            if (Document is PinnedFilesDocument)
            {
                await SaveRenderedImageAsync();
                return;
            }

            var html = Document is PinnedHtmlDocument && !IgnoreFormat;
            var extension = html ? ".html" : Document is PinnedFormulaDocument ? ".tex" : ".txt";
            var profile = SavePreferencesProvider().Normalize().ManualProfile();
            var name = Path.ChangeExtension(profile.CreateFileName(new ScreenshotNameContext(DateTime.Now)), extension);
            using var file = await StoragePickerAwaiter.WaitAsync(StorageProvider.SaveFilePickerAsync(new()
            {
                Title = UiText.Get("ScreenshotSave"), SuggestedFileName = name,
                DefaultExtension = extension.TrimStart('.'), ShowOverwritePrompt = true,
                FileTypeChoices =
                [
                    new(html ? "HTML" : Document is PinnedFormulaDocument ? "LaTeX" : "Text")
                        { Patterns = ["*" + extension] },
                ],
            }), token);
            token.ThrowIfCancellationRequested();
            if (file is null) return;
            var content = Document is PinnedHtmlDocument formatted && html
                ? PinnedHtmlFileSource.WithBase(formatted.Html, formatted.SourceUrl)
                : Document.PlainText;
            if (file.TryGetLocalPath() is { } path)
            {
                var destination = await PinnedDocumentStorage.SaveAsync(path, content, profile.ConflictPolicy, token);
                SavedPathChanged?.Invoke(destination);
            }
            else
            {
                var bytes = Encoding.UTF8.GetBytes(content);
                token.ThrowIfCancellationRequested();
                await using var stream = await file.OpenWriteAsync();
                stream.SetLength(0);
                await stream.WriteAsync(bytes, token);
                await stream.FlushAsync(token);
            }
        }

        private async Task CopyRenderedImageAsync()
        {
            var output = await CapturePreferredOutputAsync(OutputColorMode.Sdr, _lifetime.Token);
            var clipboard = Clipboard ?? throw new InvalidOperationException(UiText.Get("ClipboardFailed"));
            await ClipboardImage.CopyAsync(clipboard, output, ClipboardPreferencesProvider().Normalize(),
                StorageProvider,
                SavePreferencesProvider().Normalize().Quality, _lifetime.Token);
        }

        private async Task SaveRenderedImageAsync()
        {
            var token = _lifetime.Token;
            var output = await CapturePreferredOutputAsync(OutputColorMode.Sdr, token);
            var profile = SavePreferencesProvider().Normalize().ManualProfile();
            var path = await ScreenshotOutputStorage.PickPathAsync(this, StorageProvider, profile,
                OutputDirectoryProvider(), "PinnedImage", OutputColorMode.Sdr, AppearanceDialogStyle.Modern,
                new(DateTime.Now), token);
            if (path is null) return;
            var saved = await ScreenshotImageStorage.SaveAsync(output, path, profile, token);
            SavedPathChanged?.Invoke(saved.Paths[0]);
            await CompleteSelectionSaveAsync(saved.Paths, profile, output);
        }
    }
}

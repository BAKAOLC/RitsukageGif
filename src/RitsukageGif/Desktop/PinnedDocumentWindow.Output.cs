using Avalonia;
using Avalonia.Input;
using Avalonia.Input.Platform;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedDocumentWindow
    {
        public Task CompleteSelectionSaveAsync(IReadOnlyList<string> paths, ScreenshotSaveProfile profile,
            ScreenshotResult output)
        {
            return ScreenshotSaveCompletion.RunAsync(paths, profile, Clipboard, StorageProvider, CopyRenderedImageAsync,
                OutputNotification, _lifetime.Token);
        }

        private Task<ScreenshotResult> CapturePreferredOutputAsync(OutputColorMode mode, CancellationToken token)
        {
            if (PreferencesProvider().OutputContent == PinnedImageOutputContent.Current &&
                (_isThumbnail || !DocumentSceneViewScale.Equals(1d)))
            {
                if (mode == OutputColorMode.Hdr) throw new NotSupportedException(UiText.Get("HdrCaptureUnavailable"));
                return CaptureCurrentDocumentViewAsync(token);
            }

            var pending = ((IPinnedWindow)this).CaptureSceneOutputAsync(mode, token);
            if (PreferencesProvider().OutputContent == PinnedImageOutputContent.Original) return pending;
            return PinnedViewportOutput.ProjectAsync(pending, new(Matrix.Identity, default,
                false, Opacity, PreferencesProvider().SmoothScaling, RenderScaling), token);
        }

        private Task CopyAsync()
        {
            if (_textView is { SelectedText.Length: > 0 } text)
                return text.SelectedHtml is { } selectionHtml
                    ? CopyHtmlAsync(text.SelectedText, selectionHtml,
                        (Document as PinnedHtmlDocument)?.SourceUrl)
                    : CopyTextAsync(text.SelectedText);
            if (Document is PinnedFilesDocument files) return CopySourceFilesAsync(files.Paths);
            if (Presentation.SourceMime is not null)
                return Presentation.CopySourceMimeAsync(PinnedSourceMimeFormat.Typed);
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
            var transfer = CreateFilesTransfer(paths);
            token.ThrowIfCancellationRequested();
            var clipboard = Clipboard ?? throw new InvalidOperationException(UiText.Get("ClipboardFailed"));
            await clipboard.SetDataAsync(transfer);
            await clipboard.FlushAsync();
        }

        private DataTransfer CreateFilesTransfer(IReadOnlyList<string> paths)
        {
            var transfer = new DataTransfer();
            foreach (var path in paths)
                transfer.Add(DataTransferItem.CreateFile(new ClipboardFileReference(path, StorageProvider)));
            return transfer;
        }

        private async Task DragFilesAsync(PointerPressedEventArgs args, IReadOnlyList<string> paths)
        {
            var token = _lifetime.Token;
            var transfer = CreateFilesTransfer(paths);
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

        private async Task CopyRenderedImageAsync()
        {
            var output = await CapturePreferredOutputAsync(OutputColorMode.Sdr, _lifetime.Token);
            var clipboard = Clipboard ?? throw new InvalidOperationException(UiText.Get("ClipboardFailed"));
            await ClipboardImage.CopyAsync(clipboard, output, ClipboardPreferencesProvider().Normalize(),
                StorageProvider,
                SavePreferencesProvider().Normalize().Quality, token: _lifetime.Token);
            await SaveDocumentAutomaticallyAsync(ScreenshotAutoSaveTriggers.Copy, output);
        }

        private async Task SaveRenderedImageAsync()
        {
            var token = _lifetime.Token;
            var output = await CapturePreferredOutputAsync(OutputColorMode.Sdr, token);
            var profile = SavePreferencesProvider().Normalize().ManualProfile();
            var path = await ScreenshotOutputStorage.PickPathAsync(this, StorageProvider, profile,
                OutputDirectoryProvider(), "PinnedImage", OutputColorMode.Sdr, DialogStyleProvider(),
                new(DateTime.Now), token);
            if (path is null) return;
            var saved = await ScreenshotImageStorage.SaveAsync(output, path, profile, token);
            SavedPathChanged?.Invoke(saved.Paths[0]);
            await SaveDocumentAutomaticallyAsync(ScreenshotAutoSaveTriggers.Save, output);
            await CompleteSelectionSaveAsync(saved.Paths, profile, output);
        }
    }
}

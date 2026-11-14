namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedDocumentWindow
    {
        internal Task<ScreenshotResult> CaptureAutomationAsync(PinOutputView view, OutputColorMode mode,
            CancellationToken token)
        {
            return RunAutomationAsync(() => CaptureAutomationCoreAsync(view, mode, token));
        }

        private async Task<ScreenshotResult> CaptureAutomationCoreAsync(PinOutputView view, OutputColorMode mode,
            CancellationToken token)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
            if (mode == OutputColorMode.Hdr) throw new NotSupportedException(UiText.Get("HdrCaptureUnavailable"));
            return await (view switch
            {
                PinOutputView.Preferred => CapturePreferredOutputAsync(linked.Token),
                PinOutputView.Original => CaptureDocumentSceneAsync(true, linked.Token),
                PinOutputView.Displayed => CaptureCurrentDocumentViewAsync(linked.Token),
                _ => throw new ArgumentOutOfRangeException(nameof(view)),
            });
        }

        internal AnnotationScriptCanvas CreateAutomationCanvas(AnnotationScriptService service)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            var session = EnsureDocumentAnnotations();
            return service.Attach(session, session.Selection.Width, session.Selection.Height,
                async token => (await CaptureAutomationAsync(PinOutputView.Original, OutputColorMode.Sdr, token)).Png,
                () => !_closed && ReferenceEquals(session, _annotationScene), async token =>
                {
                    var output = await CaptureAutomationAsync(PinOutputView.Original, OutputColorMode.Both, token);
                    return output.ColorMode != OutputColorMode.Sdr && output.Hdr?.ColorInfo.IsHdrSource == true
                        ? output.Hdr
                        : null;
                });
        }

        internal Task CopyAutomationAsync(PinCopyKind kind, PinOutputView view, CancellationToken token)
        {
            return RunAutomationAsync(() => CopyAutomationCoreAsync(kind, view, token));
        }

        private async Task CopyAutomationCoreAsync(PinCopyKind kind, PinOutputView view, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            switch (kind)
            {
                case PinCopyKind.Image:
                    var output = await CaptureAutomationCoreAsync(view, OutputColorMode.Sdr, token);
                    var clipboard = Clipboard ?? throw new InvalidOperationException(UiText.Get("ClipboardFailed"));
                    await ClipboardImage.CopyAsync(clipboard, output, ClipboardPreferencesProvider().Normalize(),
                        StorageProvider, SavePreferencesProvider().Normalize().Quality, token: token);
                    await SaveDocumentAutomaticallyAsync(ScreenshotAutoSaveTriggers.Copy, output);
                    break;
                case PinCopyKind.File: await CopyAsFileAsync(); break;
                case PinCopyKind.Source: await CopyAsync(); break;
                case PinCopyKind.Text: await CopyTextAsync(Document.PlainText); break;
                case PinCopyKind.Html:
                    var html = Document switch
                    {
                        PinnedHtmlDocument rich => rich.Html,
                        PinnedColorDocument { Html.Length: > 0 } color => color.Html,
                        _ => Presentation.SourceMime?.Html,
                    };
                    if (html is null) throw new InvalidOperationException("The pin has no HTML content.");
                    await CopyHtmlAsync(Document.PlainText, html, (Document as PinnedHtmlDocument)?.SourceUrl);
                    break;
                case PinCopyKind.Animation:
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        internal Task<IReadOnlyList<string>> SaveAutomationAsync(string path, PinOutputView view,
            OutputColorMode mode, CancellationToken token)
        {
            return RunAutomationAsync(() => SaveAutomationCoreAsync(path, view, mode, token));
        }

        private async Task<IReadOnlyList<string>> SaveAutomationCoreAsync(string path, PinOutputView view,
            OutputColorMode mode, CancellationToken token)
        {
            var output = await CaptureAutomationCoreAsync(view, mode, token);
            var profile = SavePreferencesProvider().Normalize().ManualProfile();
            var saved = await ScreenshotImageStorage.SaveAsync(output, path, profile, token);
            SavedPathChanged?.Invoke(saved.Paths[0]);
            await SaveDocumentAutomaticallyAsync(ScreenshotAutoSaveTriggers.Save, output);
            await CompleteSelectionSaveAsync(saved.Paths, profile, output);
            return saved.Paths;
        }
    }
}

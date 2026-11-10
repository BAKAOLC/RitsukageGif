namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
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
            return await (view switch
            {
                PinOutputView.Preferred => CapturePreferredOutputAsync(mode, linked.Token),
                PinOutputView.Displayed => CaptureOutputAsync(mode, linked.Token),
                PinOutputView.Original => CaptureSceneOutputAsync(mode, linked.Token),
                _ => throw new ArgumentOutOfRangeException(nameof(view)),
            });
        }

        internal AnnotationScriptCanvas CreateAutomationCanvas(AnnotationScriptService service)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            var session = EnsureAnnotationScene();
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
                case PinCopyKind.Animation:
                    if (_animationEncoded is null || AnimationMetadata is null)
                        throw new NotSupportedException("The pin has no original animation.");
                    await CopyOriginalAnimationAsync();
                    break;
                case PinCopyKind.Image:
                case PinCopyKind.File:
                    var output = await CaptureAutomationCoreAsync(view, OutputColorModeProvider(), token);
                    await CopyImageAsync(output, kind == PinCopyKind.File);
                    await SaveAutomaticallyAsync(ScreenshotAutoSaveTriggers.Copy, output);
                    break;
                case PinCopyKind.Source:
                    await CopyAutomationSourceAsync(PinnedSourceMimeFormat.Typed);
                    break;
                case PinCopyKind.Text:
                    await CopyAutomationSourceAsync(PinnedSourceMimeFormat.PlainText);
                    break;
                case PinCopyKind.Html:
                    await CopyAutomationSourceAsync(PinnedSourceMimeFormat.Html);
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        private Task CopyAutomationSourceAsync(PinnedSourceMimeFormat format)
        {
            if (Presentation.SourceMime is not { } source ||
                (format == PinnedSourceMimeFormat.PlainText && !source.HasText) ||
                (format == PinnedSourceMimeFormat.Html && !source.HasHtml))
                throw new InvalidOperationException("The pin has no matching source content.");
            return Presentation.CopySourceMimeAsync(format);
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
            await SaveAutomaticallyAsync(ScreenshotAutoSaveTriggers.Save, output);
            await CompleteSelectionSaveAsync(saved.Paths, profile, output);
            return saved.Paths;
        }
    }
}

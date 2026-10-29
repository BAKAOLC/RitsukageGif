namespace RitsukageGif.Desktop
{
    internal sealed partial class SelectionCoordinator
    {
        internal Func<ScreenshotResult, CancellationToken, Task>? RecognitionOutputHandler { get; set; }
        internal RecognitionPreferences RecognitionPreferences { get; set; } = new();

        private void RecognizeSelection(RecognitionMode mode, bool copyText = false)
        {
            if (_finished || _screenshotCompleting || NeedsWindowScreenshot || ScreenshotOwner is null ||
                Annotations is not { IsBusy: false, IsDrawing: false, CanAnnotate: true } annotations ||
                Snapshot.Region.IsEmpty) return;
            var snapshot = Snapshot;
            var result =
                SelectionResult.Create(snapshot.Region, WindowSource, snapshot.Target, UsesLocalSource, _cursor);
            if (result is not null) _screenshotTask = RecognizeSelectionAsync(result, annotations, mode, copyText);
        }

        private async Task RecognizeSelectionAsync(SelectionResult result, ScreenshotAnnotationSession annotations,
            RecognitionMode mode, bool copyText)
        {
            using var work = CancellationTokenSource.CreateLinkedTokenSource(_cancel.Token);
            _screenshotWork = work;
            _screenshotCompleting = true;
            annotations.SetExportState(true);
            Refresh();
            try
            {
                var png = await ComposeSelectedScreenshotAsync(result, annotations, work.Token);
                work.Token.ThrowIfCancellationRequested();
                if (_finished || ScreenshotOwner is not { } owner) return;
                if (mode == RecognitionMode.Text && (copyText || !RecognitionPreferences.ShowDialogOnRecognition))
                {
                    var resultText = await RecognitionOutput.RecognizeAsync(png, RecognitionPreferences,
                        new OcrRecognitionService(), work.Token);
                    work.Token.ThrowIfCancellationRequested();
                    if (_finished) return;
                    if (string.IsNullOrWhiteSpace(resultText))
                    {
                        annotations.SetExportState(false, UiText.Get("RecognitionEmpty"));
                        return;
                    }

                    var clipboard = owner.Clipboard ??
                                    throw new InvalidOperationException(UiText.Get("ClipboardFailed"));
                    await clipboard.SetTextAsync(resultText);
                    work.Token.ThrowIfCancellationRequested();
                    if (RecognitionOutputHandler is { } handler)
                        await ReportRecognizedScreenshotAsync(handler, png, result, annotations, work.Token);
                    if (copyText && RecognitionPreferences.ShowDialogOnCopyAll)
                    {
                        foreach (var window in _windows) window.Topmost = false;
                        await RecognitionWindow.ShowAsync(owner, png, RecognitionMode.Text,
                            preferences: RecognitionPreferences, initialText: resultText, token: work.Token);
                    }

                    return;
                }

                foreach (var window in _windows) window.Topmost = false;
                await RecognitionWindow.ShowAsync(owner, png, mode,
                    RecognitionOutputHandler is { } output
                        ? token => ReportRecognizedScreenshotAsync(output, png, result, annotations, token)
                        : null,
                    RecognitionPreferences, token: work.Token);
            }
            catch (OperationCanceledException) when (work.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                if (!_finished) annotations.SetExportState(false, error.Message);
            }
            finally
            {
                if (ReferenceEquals(_screenshotWork, work)) _screenshotWork = null;
                _screenshotCompleting = false;
                if (!_finished)
                {
                    if (annotations.IsBusy) annotations.SetExportState(false);
                    foreach (var window in _windows) window.Topmost = true;
                    ScreenshotOwner?.Activate();
                    Refresh();
                }
            }
        }

        private async Task ReportRecognizedScreenshotAsync(
            Func<ScreenshotResult, CancellationToken, Task> output, byte[] png,
            SelectionResult selection, ScreenshotAnnotationSession annotations, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (_finished) return;
            var (hdr, hdrReason) = await ComposeSelectedHdrScreenshotAsync(selection, annotations, token);
            token.ThrowIfCancellationRequested();
            if (!_finished)
                await output(new(png, ScreenshotOutputAction.Copy, hdr, hdrReason)
                {
                    ColorMode = annotations.OutputColorMode, CaptureScaling = ScreenshotCaptureScaling,
                    Decoration = Decoration, OutputOrigin = ScreenshotOutputOrigin, OutputRegion = selection.Region,
                }, token);
        }

        private async Task<byte[]> ComposeSelectedScreenshotAsync(SelectionResult result,
            ScreenshotAnnotationSession annotations, CancellationToken token)
        {
            if (ScreenshotPreview && _screenshots.Count == 1)
            {
                if (_previewOriginalPng is { } original &&
                    (!annotations.HasAnnotations || !annotations.AnnotationsVisible) && !annotations.IsDrawing)
                    return await Task.Run(() => PngImageCodec.Crop(original, result.Region, token), token);
                var frame = _screenshots[0];
                return await annotations.ComposeAsync(result.Region,
                    new ScrollCaptureImage(frame.Pixels, frame.Region.Width, frame.Region.Height), token);
            }

            var png = await ExportScreenshotAsync(result, token);
            return await annotations.ComposeAsync(result.Region, png, token);
        }
    }
}

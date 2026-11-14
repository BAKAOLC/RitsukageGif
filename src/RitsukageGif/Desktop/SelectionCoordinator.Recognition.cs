using Avalonia.Input.Platform;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SelectionCoordinator
    {
        internal Func<ScreenshotResult, ScreenshotAutoSaveTriggers, CancellationToken, Task>? RecognitionOutputHandler
        {
            get;
            set;
        }

        internal Action? RecognitionCopied { get; set; }
        internal ScreenshotRecognitionRequest? DeferredRecognition { get; private set; }
        internal ScreenshotResult? TranslationRequested { get; private set; }
        internal RecognitionPreferences RecognitionPreferences { get; set; } = new();
        internal Func<int>? RecognitionImageSaveQuality { get; set; }
        internal TranslationPreferences TranslationPreferences { get; set; } = new();
        internal Action<FormulaCopyFormat>? FormulaCopyFormatChanged { get; set; }
        internal Action<bool>? RecognitionOriginalImageVisibilityChanged { get; set; }
        internal Action<FormulaLatexFormat>? FormulaLatexFormatChanged { get; set; }

        internal Action<RecognitionDialogPreferences, RecognitionDialogPreferences>? RecognitionDialogPreferencesChanged
        {
            get;
            set;
        }

        internal Action<RecognitionConfirmationKind>? DirectRecognitionCopyRequested { get; set; }

        private void RecognizeSelection(RecognitionMode mode, bool copyText = false, bool translate = false)
        {
            if (_finished || _screenshotCompleting || NeedsWindowScreenshot || ScreenshotOwner is null ||
                Annotations is not { IsBusy: false, IsDrawing: false, CanAnnotate: true } annotations ||
                Snapshot.Region.IsEmpty) return;
            var snapshot = Snapshot;
            var result =
                SelectionResult.Create(snapshot.Region, WindowSource, snapshot.Target, UsesLocalSource, _cursor,
                    _selectionShape,
                    ShapeMode == SelectionShapeMode.MultiWindow ? SelectedShapeWindows() : null);
            if (result is not null)
                _screenshotTask = RecognizeSelectionAsync(result, annotations, mode, copyText, translate);
        }

        private async Task RecognizeSelectionAsync(SelectionResult result, ScreenshotAnnotationSession annotations,
            RecognitionMode mode, bool copyText, bool translate)
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
                if (translate)
                {
                    var screenshot = await CreateRecognitionScreenshotAsync(png, result, annotations, work.Token);
                    var trigger = TranslationPreferences.Mode == TranslationMode.ImageToImage
                        ? ScreenshotAutoSaveTriggers.Translate | ScreenshotAutoSaveTriggers.Pin
                        : ScreenshotAutoSaveTriggers.Translate;
                    if (!_finished && RecognitionOutputHandler is { } handler)
                        await handler(screenshot, trigger, work.Token);
                    work.Token.ThrowIfCancellationRequested();
                    if (_finished) return;
                    TranslationRequested = screenshot;
                    RequestFinish(result);
                    return;
                }

                switch (mode)
                {
                    case RecognitionMode.Barcode when copyText:
                    {
                        var codes = await new BarcodeRecognitionService().RecognizeAsync(png, work.Token);
                        work.Token.ThrowIfCancellationRequested();
                        if (_finished) return;
                        var text = CodeRecognitionOutput.FirstText(codes);
                        if (text is null)
                        {
                            CompleteCodeWithoutResult();
                            return;
                        }

                        var clipboard = owner.Clipboard ??
                                        throw new InvalidOperationException(UiText.Get("ClipboardFailed"));
                        await clipboard.SetTextAsync(text);
                        await clipboard.FlushAsync();
                        work.Token.ThrowIfCancellationRequested();
                        RecognitionCopied?.Invoke();
                        if (RecognitionOutputHandler is { } codeOutput)
                            await ReportRecognizedScreenshotAsync(codeOutput, png, result, annotations,
                                ScreenshotAutoSaveTriggers.Recognize, work.Token);
                        if (!_finished) Finish(result);
                        return;
                    }
                    case RecognitionMode.Text or RecognitionMode.Table or RecognitionMode.Formula:
                    {
                        var screenshot = await CreateRecognitionScreenshotAsync(png, result, annotations, work.Token);
                        work.Token.ThrowIfCancellationRequested();
                        if (_finished) return;
                        DeferredRecognition = new(screenshot, mode);
                        Finish(result);
                        return;
                    }
                    case RecognitionMode.Barcode:
                        foreach (var window in _windows) window.Topmost = false;
                        await RecognitionWindow.ShowAsync(owner, png, mode,
                            RecognitionOutputHandler is { } output
                                ? token => ReportRecognizedScreenshotAsync(output, png, result, annotations,
                                    ScreenshotAutoSaveTriggers.Recognize, token)
                                : null,
                            RecognitionPreferences, token: work.Token, translationPreferences: TranslationPreferences,
                            copyFormatChanged: FormulaCopyFormatChanged,
                            originalImageVisibilityChanged: RecognitionOriginalImageVisibilityChanged,
                            latexFormatChanged: FormulaLatexFormatChanged,
                            imageSaveQuality: RecognitionImageSaveQuality,
                            copyDirectlyNextTime: () =>
                                DirectRecognitionCopyRequested?.Invoke(RecognitionConfirmationKind.Screenshot),
                            dialogPreferencesChanged: RecognitionDialogPreferencesChanged);
                        break;
                }
            }
            catch (OperationCanceledException) when (work.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                if (!_finished)
                {
                    if (PropagateOutputErrors) Finish(null, error);
                    else annotations.SetExportState(false, error.Message);
                }
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
            Func<ScreenshotResult, ScreenshotAutoSaveTriggers, CancellationToken, Task> output, byte[] png,
            SelectionResult selection, ScreenshotAnnotationSession annotations, ScreenshotAutoSaveTriggers trigger,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (_finished) return;
            var result = await CreateRecognitionScreenshotAsync(png, selection, annotations, token);
            token.ThrowIfCancellationRequested();
            if (!_finished) await output(result, trigger, token);
        }

        private async Task<ScreenshotResult> CreateRecognitionScreenshotAsync(byte[] png, SelectionResult selection,
            ScreenshotAnnotationSession annotations, CancellationToken token)
        {
            var (hdr, hdrReason) = await ComposeSelectedHdrScreenshotAsync(selection, annotations, token);
            return new(png, ScreenshotOutputAction.Copy, hdr, hdrReason)
            {
                ColorMode = annotations.OutputColorMode, CaptureScaling = ScreenshotCaptureScaling,
                Decoration = Decoration, OutputOrigin = ScreenshotOutputOrigin, OutputRegion = selection.Region,
            };
        }

        private async Task<byte[]> ComposeSelectedScreenshotAsync(SelectionResult result,
            ScreenshotAnnotationSession annotations, CancellationToken token)
        {
            await annotations.AutomaticWatermarkTask.WaitAsync(token);
            var png = await ComposeSelectionPixelsAsync(result, annotations, token);
            return result.Shape is { } shape
                ? await Task.Run(() => ScreenshotDecorationPng.ApplyShape(png, shape, result.Region, token), token)
                : png;
        }

        private async Task<byte[]> ComposeSelectionPixelsAsync(SelectionResult result,
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

            if (await ComposeNativeScreenshotAsync(result, annotations, token) is { } nativePng) return nativePng;
            var png = await ExportScreenshotAsync(result, token);
            return await annotations.ComposeAsync(result.Region, png, token);
        }
    }
}

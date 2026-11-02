using Avalonia.Controls;
using Avalonia.Input.Platform;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SelectionCoordinator
    {
        internal Func<ScreenshotResult, CancellationToken, Task>? RecognitionOutputHandler { get; set; }
        internal ReadOnlyMemory<byte> TranslationRequestedPng { get; private set; }
        internal RecognitionPreferences RecognitionPreferences { get; set; } = new();
        internal TranslationPreferences TranslationPreferences { get; set; } = new();
        internal Action<Window, IReadOnlyList<string>>? PinFormulaRequested { get; set; }
        internal Action<FormulaCopyFormat>? FormulaCopyFormatChanged { get; set; }
        internal Action<bool>? RecognitionOriginalImageVisibilityChanged { get; set; }
        internal Action<FormulaLatexFormat>? FormulaLatexFormatChanged { get; set; }

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
                    if (RecognitionOutputHandler is { } handler)
                        await ReportRecognizedScreenshotAsync(handler, png, result, annotations, work.Token);
                    work.Token.ThrowIfCancellationRequested();
                    if (_finished) return;
                    TranslationRequestedPng = png;
                    RequestFinish(result);
                    return;
                }

                // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
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
                        if (RecognitionOutputHandler is { } codeOutput)
                            await ReportRecognizedScreenshotAsync(codeOutput, png, result, annotations, work.Token);
                        if (!_finished) Finish(result);
                        return;
                    }

                    case RecognitionMode.Text when copyText || !RecognitionPreferences.ShowDialogOnRecognition:
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
                        await clipboard.FlushAsync();
                        work.Token.ThrowIfCancellationRequested();
                        if (RecognitionOutputHandler is { } handler)
                            await ReportRecognizedScreenshotAsync(handler, png, result, annotations, work.Token);
                        if (copyText && RecognitionPreferences.ShowDialogOnCopyAll)
                        {
                            foreach (var window in _windows) window.Topmost = false;
                            await RecognitionWindow.ShowAsync(owner, png, RecognitionMode.Text,
                                preferences: RecognitionPreferences, initialText: resultText, token: work.Token,
                                translationPreferences: TranslationPreferences);
                        }

                        return;
                    }
                }

                foreach (var window in _windows) window.Topmost = false;
                await RecognitionWindow.ShowAsync(owner, png, mode,
                    RecognitionOutputHandler is { } output
                        ? token => ReportRecognizedScreenshotAsync(output, png, result, annotations, token)
                        : null,
                    RecognitionPreferences, token: work.Token, translationPreferences: TranslationPreferences,
                    pinFormula: PinFormulaRequested, copyFormatChanged: FormulaCopyFormatChanged,
                    originalImageVisibilityChanged: RecognitionOriginalImageVisibilityChanged,
                    latexFormatChanged: FormulaLatexFormatChanged);
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

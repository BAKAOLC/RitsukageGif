using Avalonia;
using Avalonia.Media.Imaging;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedDocumentWindow
    {
        async Task<ScreenshotResult> IPinnedWindow.CaptureSceneOutputAsync(OutputColorMode mode,
            CancellationToken token)
        {
            if (mode == OutputColorMode.Hdr) throw new NotSupportedException(UiText.Get("HdrCaptureUnavailable"));
            _annotationOutputs++;
            ScreenshotAnnotationSession? session = null;
            var wasBusy = false;
            try
            {
                if (_annotationRefreshing) await _annotationRefresh.WaitAsync(token);
                _lifetime.Token.ThrowIfCancellationRequested();
                session = _annotationScene;
                wasBusy = session?.IsBusy ?? false;
                if (session is { HasMosaicAnnotations: true }) await EnsureDocumentAnnotationSourceAsync(token);
                session?.SetExportState(true);
                using var bitmap = await RenderDocumentSceneBaseAsync(token);
                await DrawDocumentAnnotationsAsync(bitmap, default, 1, token);
                return await EncodeDocumentBitmapAsync(bitmap, token);
            }
            finally
            {
                if (!_closed && ReferenceEquals(session, _annotationScene)) session?.SetExportState(wasBusy);
                _annotationOutputs--;
                TryReleaseDocumentSources();
            }
        }

        private async Task DrawDocumentAnnotationsAsync(RenderTargetBitmap bitmap, Point sourceOrigin,
            double viewScale, CancellationToken token)
        {
            if (_annotationScene is not { HasAnnotations: true, AnnotationsVisible: true } session) return;
            if (session.HasMosaicAnnotations) await EnsureDocumentAnnotationSourceAsync(token);
            token.ThrowIfCancellationRequested();
            session.CommitTextEdit();
            using var context = bitmap.CreateDrawingContext(false);
            using var transform = context.PushTransform(new(viewScale / _annotationScaling, 0, 0,
                viewScale / _annotationScaling, -sourceOrigin.X * viewScale, -sourceOrigin.Y * viewScale));
            session.Render(context);
        }

        private static async Task<ScreenshotResult> EncodeDocumentBitmapAsync(RenderTargetBitmap bitmap,
            CancellationToken token)
        {
            using var stream = new MemoryStream();
            await Task.Run(() => bitmap.Save(stream, PngBitmapEncoderOptions.Default), token);
            token.ThrowIfCancellationRequested();
            return new(stream.ToArray(), ScreenshotOutputAction.Save)
                { ColorMode = OutputColorMode.Sdr, CaptureScaling = bitmap.Dpi.X / 96 };
        }

        private async Task CompleteDocumentAnnotationAsync(ScreenshotOutputAction action)
        {
            // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
            switch (action)
            {
                case ScreenshotOutputAction.Copy: await CopyRenderedImageAsync(); break;
                case ScreenshotOutputAction.Save: await SaveRenderedImageAsync(); break;
                case ScreenshotOutputAction.Pin: EndDocumentMark(); break;
                case ScreenshotOutputAction.QuickSave: await QuickSaveRenderedImageAsync(); break;
                case ScreenshotOutputAction.ExportPdf: await ExportDocumentImageAsync(); break;
                case ScreenshotOutputAction.Print: await PrintDocumentImageAsync(); break;
            }
        }

        private async Task RecognizeDocumentImageAsync(RecognitionMode mode)
        {
            var token = _lifetime.Token;
            var output = await ((IPinnedWindow)this).CaptureSceneOutputAsync(OutputColorMode.Sdr, token);
            await RecognitionWindow.ShowAsync(this, output.Png, mode,
                preferences: RecognitionPreferencesProvider(), token: token,
                imageSaveQuality: () => SavePreferencesProvider().Quality);
        }

        private async Task TranslateDocumentImageAsync()
        {
            var token = _lifetime.Token;
            var output = await ((IPinnedWindow)this).CaptureSceneOutputAsync(OutputColorMode.Sdr, token);
            if (ImageTranslationRequested is not null) await ImageTranslationRequested(this, output.Png, token);
            else
                await ImageTranslationWindow.ShowAsync(this, output.Png,
                    TranslationPreferencesProvider(), RecognitionPreferencesProvider(),
                    TextRecognitionServiceProvider(), token: token);
        }
    }
}

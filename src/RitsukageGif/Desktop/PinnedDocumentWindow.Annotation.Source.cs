namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedDocumentWindow
    {
        private async Task RefreshDocumentAnnotationSourceAsync(CancellationToken token)
        {
            var session = _annotationScene!;
            var version = _annotationSourceVersion;
            var wasBusy = session.IsBusy;
            session.SetExportState(true);
            try
            {
                using var bitmap = await RenderDocumentSceneBaseAsync(token, _annotationScaling);
                token.ThrowIfCancellationRequested();
                using var buffer = new ImagePreviewFramebuffer(bitmap.PixelSize);
                bitmap.CopyPixels(buffer);
                token.ThrowIfCancellationRequested();
                await session.SetOverlayFrameAsync(new(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height),
                    buffer.Pixels, token);
                token.ThrowIfCancellationRequested();
                _annotationSourcePixels = buffer.Pixels;
                _annotationAppliedVersion = version;
                _annotationFailedVersion = -1;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                _annotationFailedVersion = version;
                Logger.ZLogError(error, $"Pinned document annotation source could not be rendered.");
                if (!_closed)
                {
                    EndDocumentMark();
                    _status.Text = error.Message;
                    _status.IsVisible = true;
                }
            }
            finally
            {
                _annotationRefreshing = false;
                if (!_closed) session.SetExportState(wasBusy);
                TryReleaseDocumentSources();
            }
        }

        private async Task EnsureDocumentAnnotationSourceAsync(CancellationToken token)
        {
            if (_annotationScene is null) return;
            if (_annotationRefreshing) await _annotationRefresh.WaitAsync(token);
            token.ThrowIfCancellationRequested();
            if (_annotationAppliedVersion == _annotationSourceVersion) return;
            _annotationRefreshing = true;
            _annotationRefresh = RefreshDocumentAnnotationSourceAsync(token);
            await _annotationRefresh;
            token.ThrowIfCancellationRequested();
            if (_annotationAppliedVersion != _annotationSourceVersion)
                throw new InvalidOperationException("The document annotation background is unavailable.");
        }

        private async Task DetectDocumentPrivacyAsync(ScreenshotAnnotationSession session, long request)
        {
            var token = session.PrivacyDetectionToken;
            try
            {
                await EnsureDocumentAnnotationSourceAsync(token);
                token.ThrowIfCancellationRequested();
                if (!ReferenceEquals(_annotationScene, session) || !IsMarkEditing) return;
                var region = session.Selection;
                var frame = new CapturedFrame(region.Width, region.Height, checked(region.Width * 4),
                    CapturePixelFormat.Bgra8Srgb, _annotationSourcePixels!, new(false), CaptureAlphaMode.Straight, 0,
                    []);
                var service = PrivacyDetectionService.ForAnnotations(TextRecognitionServiceProvider(),
                    new BarcodeRecognitionService(), RecognitionPreferencesProvider().LanguageTag);
                var result = await service.DetectAsync(frame, token);
                token.ThrowIfCancellationRequested();
                if (!ReferenceEquals(_annotationScene, session) || !IsMarkEditing) return;
                if (session.SetPrivacyCandidates(request, result.Candidates,
                        AnnotationPreferencesProvider().AutoSelectPrivacyMosaic) && result.Failures.Count > 0)
                    session.SetPrivacyNotice(string.Join(Environment.NewLine,
                        result.Failures.Select(failure => failure.Message)));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                if (!_closed && ReferenceEquals(_annotationScene, session))
                    session.FailPrivacyDetection(request, error.Message);
            }
        }
    }
}

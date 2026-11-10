namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        private async Task DetectPinnedPrivacyAsync(ScreenshotAnnotationSession session, long request,
            PixelRect region, bool autoSelect, CancellationToken token)
        {
            try
            {
                var frame = new CapturedFrame(region.Width, region.Height, checked(region.Width * 4),
                    CapturePixelFormat.Bgra8Srgb, EffectivePixels, new(false), CaptureAlphaMode.Straight, 0, []);
                var service = PrivacyDetectionService.ForAnnotations(
                    TextRecognitionServiceProvider?.Invoke() ?? DefaultTextRecognition,
                    new BarcodeRecognitionService(), RecognitionPreferencesProvider().LanguageTag);
                var detected = await service.DetectAsync(frame, token);
                token.ThrowIfCancellationRequested();
                if (!ReferenceEquals(session, _annotation)) return;
                if (session.SetPrivacyCandidates(request, detected.Candidates, autoSelect) &&
                    detected.Failures.Count > 0)
                    session.SetPrivacyNotice(string.Join(Environment.NewLine,
                        detected.Failures.Select(failure => failure.Message)));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                if (ReferenceEquals(session, _annotation)) session.FailPrivacyDetection(request, error.Message);
            }
        }
    }
}

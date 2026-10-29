using ImageMagick;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SelectionCoordinator
    {
        private bool _autoSelectPrivacyMosaic = true;

        private async Task DetectPrivacyAsync(long request, ScreenshotAnnotationSession annotations,
            CancellationToken token)
        {
            try
            {
                var snapshot = Snapshot;
                var result = SelectionResult.Create(snapshot.Region, WindowSource, snapshot.Target,
                    UsesLocalSource, _cursor);
                if (_finished || NeedsWindowScreenshot || result is null)
                {
                    annotations.FailPrivacyDetection(request, null);
                    return;
                }

                var png = await ComposeSelectedScreenshotAsync(result, annotations, token);
                var frame = await Task.Run(() => DecodePrivacyFrame(png, token), token);
                var service = new PrivacyDetectionService([
                    new OcrPrivacyCandidateProvider(new OcrRecognitionService(), RecognitionPreferences.LanguageTag),
                    new BarcodePrivacyCandidateProvider(new BarcodeRecognitionService()),
                ]);
                var detected = await service.DetectAsync(frame, token);
                token.ThrowIfCancellationRequested();
                if (_finished) return;
                var translated = detected.Candidates.Select(candidate => candidate with
                {
                    Bounds = new(candidate.Bounds.X + result.Region.X, candidate.Bounds.Y + result.Region.Y,
                        candidate.Bounds.Width, candidate.Bounds.Height),
                }).ToArray();
                if (annotations.SetPrivacyCandidates(request, translated, _autoSelectPrivacyMosaic) &&
                    detected.Failures.Count > 0)
                    annotations.SetExportState(false, string.Join(Environment.NewLine,
                        detected.Failures.Select(failure => failure.Message)));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                if (!_finished) annotations.FailPrivacyDetection(request, error.Message);
            }
        }

        private static CapturedFrame DecodePrivacyFrame(byte[] png, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var info = new MagickImageInfo(png);
            if ((long)info.Width * info.Height > 16 * 1024 * 1024)
                throw new InvalidOperationException(UiText.Get("RecordingTooLarge"));
            using var image = new MagickImage(png);
            image.ColorSpace = ColorSpace.sRGB;
            image.Depth = 8;
            using var pixels = image.GetPixels();
            var data = pixels.ToByteArray(PixelMapping.BGRA)!;
            token.ThrowIfCancellationRequested();
            return new((int)image.Width, (int)image.Height, checked((int)image.Width * 4),
                CapturePixelFormat.Bgra8Srgb, data, new(false), CaptureAlphaMode.Straight, 0, []);
        }
    }
}

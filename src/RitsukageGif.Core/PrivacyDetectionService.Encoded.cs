using ImageMagick;

namespace RitsukageGif.Core
{
    public sealed partial class PrivacyDetectionService
    {
        private static readonly SemaphoreSlim DecodeGate = new(2);

        public async Task<PrivacyDetectionResult> DetectAsync(ReadOnlyMemory<byte> encodedImage,
            string? languageTag, CancellationToken token = default)
        {
            if (encodedImage.IsEmpty || encodedImage.Length > 64 * 1024 * 1024)
                throw new ArgumentOutOfRangeException(nameof(encodedImage));
            if (languageTag is not null && (languageTag.Length > 85 ||
                                            languageTag.Any(character =>
                                                !char.IsAsciiLetterOrDigit(character) && character != '-')))
                throw new ArgumentException("Invalid OCR language tag.", nameof(languageTag));
            await DecodeGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                var frame = await Task.Run(() => DecodePrivacyImage(encodedImage, token), token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                var detector = languageTag is null
                    ? this
                    : new(_providers.Select(provider =>
                        provider is OcrPrivacyCandidateProvider ocr ? ocr.WithLanguage(languageTag) : provider));
                return await detector.DetectAsync(frame, token).ConfigureAwait(false);
            }
            catch (MagickException) when (token.IsCancellationRequested)
            {
                throw new OperationCanceledException(token);
            }
            finally
            {
                DecodeGate.Release();
            }
        }

        // ReSharper disable once UnusedMember.Global
        public Task<PrivacyDetectionResult> DetectAsync(ReadOnlyMemory<byte> encodedImage,
            CancellationToken token = default)
        {
            return DetectAsync(encodedImage, null, token);
        }

        private static CapturedFrame DecodePrivacyImage(ReadOnlyMemory<byte> encodedImage, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using var image = new MagickImage();
            image.Progress += (_, progress) => progress.Cancel = token.IsCancellationRequested;
            var settings = new MagickReadSettings { FrameCount = 1 };
            image.Ping(encodedImage.Span, settings);
            ValidatePrivacyDimensions(image.Width, image.Height);
            token.ThrowIfCancellationRequested();
            image.Read(encodedImage.Span, settings);
            image.AutoOrient();
            ValidatePrivacyDimensions(image.Width, image.Height);
            if (image.GetColorProfile() is not null && !image.TransformColorSpace(ColorProfiles.SRGB))
                throw new InvalidDataException("Image color profile could not be converted to sRGB.");
            image.ColorSpace = ColorSpace.sRGB;
            token.ThrowIfCancellationRequested();
            using var pixels = image.GetPixels();
            return new((int)image.Width, (int)image.Height, checked((int)image.Width * 4),
                CapturePixelFormat.Bgra8Srgb, pixels.ToByteArray(PixelMapping.BGRA)!, new(false),
                image.HasAlpha ? CaptureAlphaMode.Straight : CaptureAlphaMode.Opaque, 0, []);
        }

        private static void ValidatePrivacyDimensions(uint width, uint height)
        {
            if (width is 0 or > 32768 || height is 0 or > 32768 || (long)width * height > 16 * 1024 * 1024)
                throw new ArgumentOutOfRangeException(nameof(width));
        }
    }
}

using System.Runtime.InteropServices;
using ImageMagick;

namespace RitsukageGif.Desktop
{
    internal sealed class OcrRecognitionService : ITextRecognitionService
    {
        private const int MaximumPixels = 16 * 1024 * 1024;
        private const int MaximumEncodedBytes = 64 * 1024 * 1024;
        private static readonly SemaphoreSlim Gate = new(2);

        public Task<OcrCapabilities> GetCapabilitiesAsync(CancellationToken token = default)
        {
            return Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                if (!OperatingSystem.IsWindows())
                    return new(false, 0, [], "System OCR is available only on Windows.");
                try
                {
                    return WindowsOcrRecognition.GetCapabilities();
                }
                catch (Exception error) when (error is COMException
                                                  or DllNotFoundException or EntryPointNotFoundException)
                {
                    return new(false, 0, [], error.Message);
                }
            }, token);
        }

        public Task<OcrRecognitionResult> RecognizeImageAsync(ReadOnlyMemory<byte> encoded,
            string? languageTag = null, CancellationToken token = default)
        {
            if (encoded.IsEmpty || encoded.Length > MaximumEncodedBytes)
                throw new ArgumentOutOfRangeException(nameof(encoded));
            return Run(() =>
            {
                EnsurePlatform();
                token.ThrowIfCancellationRequested();
                var capabilities = WindowsOcrRecognition.GetCapabilities();
                EnsureAvailable(capabilities);
                using var image = new MagickImage();
                image.Progress += (_, progress) => progress.Cancel = token.IsCancellationRequested;
                var settings = new MagickReadSettings { FrameCount = 1 };
                try
                {
                    image.Ping(encoded.Span, settings);
                    ValidateDimensions(image.Width, image.Height, capabilities.MaxImageDimension);
                    token.ThrowIfCancellationRequested();
                    image.Read(encoded.Span, settings);
                    image.AutoOrient();
                    ValidateDimensions(image.Width, image.Height, capabilities.MaxImageDimension);
                    image.BackgroundColor = MagickColors.White;
                    image.Alpha(AlphaAction.Remove);
                    image.ColorSpace = ColorSpace.sRGB;
                }
                catch (MagickException) when (token.IsCancellationRequested)
                {
                    throw new OperationCanceledException(token);
                }

                token.ThrowIfCancellationRequested();
                return WindowsOcrRecognition.Recognize(image.ToByteArray(MagickFormat.Bgra),
                    checked((int)image.Width), checked((int)image.Height), languageTag, token);
            }, token);
        }

        public Task<OcrRecognitionResult> RecognizeBgraAsync(ReadOnlyMemory<byte> pixels,
            int width, int height, int stride, string? languageTag = null, CancellationToken token = default)
        {
            ValidateDimensions(width, height, int.MaxValue);
            if (stride < (long)width * 4 || (long)stride * height > pixels.Length)
                throw new ArgumentException("BGRA dimensions and stride exceed the supplied pixel data.",
                    nameof(pixels));
            return Run(() =>
            {
                EnsurePlatform();
                token.ThrowIfCancellationRequested();
                var capabilities = WindowsOcrRecognition.GetCapabilities();
                EnsureAvailable(capabilities);
                ValidateDimensions(width, height, capabilities.MaxImageDimension);
                var opaque = new byte[checked(width * height * 4)];
                for (var y = 0; y < height; y++)
                {
                    token.ThrowIfCancellationRequested();
                    var source = pixels.Span.Slice(y * stride, width * 4);
                    var target = opaque.AsSpan(y * width * 4, width * 4);
                    for (var x = 0; x < target.Length; x += 4)
                    {
                        var alpha = source[x + 3];
                        for (var channel = 0; channel < 3; channel++)
                            target[x + channel] =
                                (byte)((source[x + channel] * alpha + 255 * (255 - alpha) + 127) / 255);
                        target[x + 3] = 255;
                    }
                }

                return WindowsOcrRecognition.Recognize(opaque, width, height, languageTag, token);
            }, token);
        }

        private static async Task<OcrRecognitionResult> Run(Func<OcrRecognitionResult> operation,
            CancellationToken token)
        {
            await Gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                return await Task.Run(operation, token).ConfigureAwait(false);
            }
            catch (MagickException) when (token.IsCancellationRequested)
            {
                throw new OperationCanceledException(token);
            }
            finally
            {
                Gate.Release();
            }
        }

        private static void EnsurePlatform()
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("System OCR requires Windows.");
        }

        private static void EnsureAvailable(OcrCapabilities capabilities)
        {
            if (!capabilities.Available) throw new NotSupportedException(capabilities.FailureReason);
        }

        private static void ValidateDimensions(long width, long height, int maximum)
        {
            if (width <= 0 || height <= 0 || width > maximum || height > maximum || width * height > MaximumPixels)
                throw new ArgumentOutOfRangeException(nameof(width),
                    $"OCR dimensions must be between 1 and {maximum} pixels per axis and contain at most {MaximumPixels} pixels.");
        }
    }
}

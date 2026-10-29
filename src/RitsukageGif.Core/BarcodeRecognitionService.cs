using System.Text;
using ImageMagick;
using ZXing;

namespace RitsukageGif.Core
{
    public sealed partial class BarcodeRecognitionService : IBarcodeRecognitionService
    {
        private const int MaximumPixels = 16 * 1024 * 1024;
        private const int MaximumEncodedBytes = 64 * 1024 * 1024;
        private static readonly ILogger Logger = LogHub.GetLogger("Core.Recognition");
        private static readonly SemaphoreSlim Gate = new(2);

        static BarcodeRecognitionService()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        /// <summary>
        ///     Recognizes codes on a worker thread. Points identify decoder features, not the complete symbol boundary.
        ///     Cancellation is checked between decoder calls; an in-progress ZXing call is not interruptible.
        /// </summary>
        public Task<IReadOnlyList<BarcodeRecognitionResult>> RecognizeAsync(CapturedFrame frame,
            CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(frame);
            ValidateDimensions(frame.Width, frame.Height);
            if (frame.PixelFormat != CapturePixelFormat.Bgra8Srgb || !Enum.IsDefined(frame.AlphaMode) ||
                frame.Stride < (long)frame.Width * 4 || frame.Pixels.Length < (long)frame.Stride * frame.Height)
                throw new ArgumentException(null, nameof(frame));
            return Run(() => Recognize(ToLuminance(frame, token), frame.Width, frame.Height, token), token);
        }

        /// <summary>Decodes the first image frame. Result coordinates refer to its EXIF-oriented image.</summary>
        public Task<IReadOnlyList<BarcodeRecognitionResult>> RecognizeAsync(ReadOnlyMemory<byte> encodedImage,
            CancellationToken token = default)
        {
            if (encodedImage.IsEmpty || encodedImage.Length > MaximumEncodedBytes)
                throw new ArgumentOutOfRangeException(nameof(encodedImage));
            return Run(() =>
            {
                token.ThrowIfCancellationRequested();
                using var image = new MagickImage();
                image.Progress += (_, progress) => progress.Cancel = token.IsCancellationRequested;
                var settings = new MagickReadSettings { FrameCount = 1 };
                image.Ping(encodedImage.Span, settings);
                ValidateDimensions(checked((int)image.Width), checked((int)image.Height));
                token.ThrowIfCancellationRequested();
                image.Read(encodedImage.Span, settings);
                image.AutoOrient();
                ValidateDimensions(checked((int)image.Width), checked((int)image.Height));
                image.ColorSpace = ColorSpace.sRGB;
                image.BackgroundColor = MagickColors.White;
                image.Alpha(AlphaAction.Remove);
                using var pixels = image.GetPixels();
                var bgra = pixels.ToByteArray(PixelMapping.BGRA)!;
                var frame = new CapturedFrame((int)image.Width, (int)image.Height, checked((int)image.Width * 4),
                    CapturePixelFormat.Bgra8Srgb, bgra, new(false), CaptureAlphaMode.Opaque, 0, []);
                return Recognize(ToLuminance(frame, token), frame.Width, frame.Height, token);
            }, token);
        }

        private static async Task<IReadOnlyList<BarcodeRecognitionResult>> Run(
            Func<IReadOnlyList<BarcodeRecognitionResult>> operation, CancellationToken token)
        {
            await Gate.WaitAsync(token).ConfigureAwait(false);
            Logger.ZLogDebug($"Barcode recognition started");
            try
            {
                var result = await Task.Run(operation, token).ConfigureAwait(false);
                Logger.ZLogDebug($"Barcode recognition completed: results={result.Count}");
                return result;
            }
            catch (MagickException) when (token.IsCancellationRequested)
            {
                Logger.ZLogDebug($"Barcode recognition canceled during native decoding");
                throw new OperationCanceledException(token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                Logger.ZLogDebug($"Barcode recognition canceled");
                throw;
            }
            catch (Exception error)
            {
                Logger.ZLogWarning(error,
                    $"Barcode recognition failed: exceptionType={error.GetType().Name}, hResult={error.HResult}");
                throw;
            }
            finally
            {
                Gate.Release();
            }
        }

        private static void ValidateDimensions(int width, int height)
        {
            if (width <= 0 || height <= 0 || width > 32768 || height > 32768 ||
                (long)width * height > MaximumPixels)
                throw new ArgumentOutOfRangeException(nameof(width));
        }

        private static byte[] ToLuminance(CapturedFrame frame, CancellationToken token)
        {
            var result = new byte[checked(frame.Width * frame.Height)];
            for (var y = 0; y < frame.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                var row = frame.Pixels.Span.Slice(y * frame.Stride, frame.Width * 4);
                for (var x = 0; x < frame.Width; x++)
                {
                    var offset = x * 4;
                    var luminance = (row[offset] * 29 + row[offset + 1] * 150 + row[offset + 2] * 77 + 128) >> 8;
                    var alpha = frame.AlphaMode == CaptureAlphaMode.Opaque ? 255 : row[offset + 3];
                    result[y * frame.Width + x] = (byte)(frame.AlphaMode == CaptureAlphaMode.Premultiplied
                        ? Math.Min(255, luminance + 255 - alpha)
                        : (luminance * alpha + 255 * (255 - alpha) + 127) / 255);
                }
            }

            return result;
        }

        private static BarcodeReaderGeneric CreateReader(bool qrOnly)
        {
            return new()
            {
                AutoRotate = false,
                Options = new()
                {
                    TryHarder = true,
                    TryInverted = true,
                    PossibleFormats = qrOnly
                        ? [BarcodeFormat.QR_CODE]
                        :
                        [
                            BarcodeFormat.DATA_MATRIX, BarcodeFormat.AZTEC, BarcodeFormat.PDF_417,
                            BarcodeFormat.CODE_128, BarcodeFormat.CODE_39, BarcodeFormat.CODE_93,
                            BarcodeFormat.EAN_13, BarcodeFormat.EAN_8, BarcodeFormat.UPC_A, BarcodeFormat.UPC_E,
                            BarcodeFormat.ITF, BarcodeFormat.CODABAR, BarcodeFormat.RSS_14,
                            BarcodeFormat.RSS_EXPANDED,
                        ],
                },
            };
        }
    }
}

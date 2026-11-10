using System.Globalization;
using ImageMagick;

namespace RitsukageGif.Core
{
    public sealed class OnnxTextRecognitionService(StructuredRecognitionModelPaths paths)
        : ITextRecognitionService, IAsyncDisposable
    {
        private const long MaximumPixels = 16 * 1024 * 1024;

        private static readonly IReadOnlyList<OcrLanguage> Languages = Array.AsReadOnly<OcrLanguage>([
            .. new[]
            {
                "zh-Hans", "zh-Hant", "en", "ja", "fr", "de", "es", "pt", "it", "vi", "nl", "sv", "da", "nb",
                "pl", "cs", "ro", "hu", "fi", "tr", "id", "ms",
            }.Select(tag =>
            {
                var culture = CultureInfo.GetCultureInfo(tag);
                return new OcrLanguage(tag, culture.DisplayName, culture.NativeName);
            }),
        ]);

        private readonly SemaphoreSlim _gate = new(1);
        private bool _disposed;
        private OnnxRuntimeLibrary? _runtime;
        private OnnxTextRegionDetector? _detector;
        private OnnxTextLineRecognizer? _recognizer;

        public async ValueTask DisposeAsync()
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_disposed) return;
                _disposed = true;
                _recognizer?.Dispose();
                _detector?.Dispose();
                _runtime?.Dispose();
            }
            finally
            {
                _gate.Release();
            }
        }

        public Task<OcrCapabilities> GetCapabilitiesAsync(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            var available = paths.Capabilities("PP-OCRv6_det_tiny.onnx", "PP-OCRv6_rec_small.onnx");
            return Task.FromResult(new OcrCapabilities(available.Available, 32768,
                available.Available ? Languages : [], available.UnavailableReason));
        }

        public Task<OcrRecognitionResult> RecognizeImageAsync(ReadOnlyMemory<byte> encoded,
            string? languageTag = null, CancellationToken token = default)
        {
            return Run(() => RecognitionImageInput.Decode(encoded, MaximumPixels), languageTag, token);
        }

        public Task<OcrRecognitionResult> RecognizeBgraAsync(ReadOnlyMemory<byte> pixels, int width, int height,
            int stride, string? languageTag = null, CancellationToken token = default)
        {
            if (width <= 0 || height <= 0 || (long)width * height > MaximumPixels ||
                stride < (long)width * 4 || (long)stride * height > pixels.Length)
                throw new ArgumentException("OCR dimensions and stride exceed the supplied pixel data.",
                    nameof(pixels));
            return Run(() =>
            {
                if (stride == (long)width * 4)
                    return new(pixels.Span, new PixelReadSettings((uint)width, (uint)height,
                        StorageType.Char, "BGRA"));
                var dense = new byte[checked(width * height * 4)];
                for (var y = 0; y < height; y++)
                {
                    token.ThrowIfCancellationRequested();
                    pixels.Span.Slice(y * stride, width * 4).CopyTo(dense.AsSpan(y * width * 4));
                }

                return new(dense, new PixelReadSettings((uint)width, (uint)height,
                    StorageType.Char, "BGRA"));
            }, languageTag, token);
        }

        private async Task<OcrRecognitionResult> Run(Func<MagickImage> read, string? languageTag,
            CancellationToken token)
        {
            await _gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                var capabilities = await GetCapabilitiesAsync(token).ConfigureAwait(false);
                if (!capabilities.Available) throw new NotSupportedException(capabilities.FailureReason);
                if (!string.IsNullOrEmpty(languageTag) && !Languages.Any(language =>
                        language.Tag.Equals(languageTag, StringComparison.OrdinalIgnoreCase) ||
                        languageTag.StartsWith(language.Tag + "-", StringComparison.OrdinalIgnoreCase)))
                    throw new NotSupportedException("The offline text model does not support the requested language: " +
                                                    languageTag);
                return await Task.Run(() =>
                {
                    using var image = read();
                    image.BackgroundColor = MagickColors.White;
                    image.Alpha(AlphaAction.Remove);
                    image.ColorSpace = ColorSpace.sRGB;
                    image.Depth = 8;
                    return Recognize(image, languageTag, token);
                }, token).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        private OcrRecognitionResult Recognize(MagickImage image, string? languageTag, CancellationToken token)
        {
            EnsureEngine();
            var detected = _detector!.Detect(image, token);
            var regions = ReadingOrder(detected);
            TrimOverlaps(regions);
            var crops = new List<MagickImage>(regions.Count);
            try
            {
                foreach (var region in regions)
                {
                    token.ThrowIfCancellationRequested();
                    crops.Add(Crop(image, region));
                }

                var predictions = _recognizer!.Recognize(crops, token);
                var lines = new List<OcrLine>(predictions.Length);
                for (var index = 0; index < predictions.Length; index++)
                {
                    var prediction = predictions[index];
                    if (prediction.Confidence < .5 || string.IsNullOrWhiteSpace(prediction.Text)) continue;
                    var region = regions[index];
                    var bounds = Bounds(region, 0, 1, image.Width, image.Height);
                    if (bounds.Width <= 0 || bounds.Height <= 0) continue;
                    var words = prediction.Units.Where(unit => !string.IsNullOrWhiteSpace(unit.Text))
                        .Select(unit => new OcrWord(unit.Text,
                            Bounds(region, unit.Start, unit.End, image.Width, image.Height)))
                        .Where(word => word.Bounds is { Width: > 0, Height: > 0 }).ToArray();
                    var line = new OcrLine(prediction.Text, bounds, words);
                    if (lines.Count > 0 && region.Angle is > -15 and < 15 && SameRow(lines[^1].Bounds, bounds))
                    {
                        var previous = lines[^1];
                        var separator = NeedsSpace(previous.Text, line.Text) ? " " : "";
                        var left = Math.Min(previous.Bounds.X, bounds.X);
                        var top = Math.Min(previous.Bounds.Y, bounds.Y);
                        var right = Math.Max(previous.Bounds.X + previous.Bounds.Width, bounds.X + bounds.Width);
                        var bottom = Math.Max(previous.Bounds.Y + previous.Bounds.Height, bounds.Y + bounds.Height);
                        lines[^1] = new(previous.Text + separator + line.Text,
                            new(left, top, right - left, bottom - top), [.. previous.Words, .. words]);
                    }
                    else
                    {
                        lines.Add(line);
                    }
                }

                var text = string.Join(Environment.NewLine, lines.Select(line => line.Text));
                var language = languageTag ?? (text.Any(character => character is >= '\u3040' and <= '\u30ff')
                    ? "ja"
                    : "und");
                double? angle = regions.Count == 0
                    ? null
                    : regions.Select(region => region.Angle).Order().ElementAt(regions.Count / 2);
                return new(text, language, checked((int)image.Width), checked((int)image.Height), angle, lines);
            }
            finally
            {
                foreach (var crop in crops) crop.Dispose();
            }
        }

        private void EnsureEngine()
        {
            if (_runtime is not null) return;
            var runtime = new OnnxRuntimeLibrary(paths.RuntimeLibrary);
            OnnxTextRegionDetector? detector = null;
            try
            {
                detector = new(runtime, paths.Directory);
                _recognizer = new(runtime, paths.Directory);
                _detector = detector;
                _runtime = runtime;
            }
            catch
            {
                detector?.Dispose();
                runtime.Dispose();
                throw;
            }
        }

        private static bool SameRow(OcrBounds previous, OcrBounds current)
        {
            var overlap = Math.Min(previous.Y + previous.Height, current.Y + current.Height) -
                          Math.Max(previous.Y, current.Y);
            return overlap > Math.Min(previous.Height, current.Height) * .5 &&
                   current.X - previous.X - previous.Width < Math.Max(previous.Height, current.Height) * 4;
        }

        private static bool NeedsSpace(string previous, string current)
        {
            var last = previous[^1];
            var first = current[0];
            return char.IsLetterOrDigit(last) && char.IsLetterOrDigit(first) &&
                   last < '\u2e80' && first < '\u2e80';
        }

        private static void TrimOverlaps(List<DetectedTextRegion> regions)
        {
            for (var index = 1; index < regions.Count; index++)
            {
                var previous = regions[index - 1];
                var current = regions[index];
                if (Math.Abs(previous.Angle) >= 15 || Math.Abs(current.Angle) >= 15 ||
                    Math.Abs(previous.CenterY - current.CenterY) >= Math.Min(previous.Height, current.Height) * .5)
                    continue;
                var previousCosine = Math.Cos(previous.Angle * Math.PI / 180);
                var currentCosine = Math.Cos(current.Angle * Math.PI / 180);
                if (previous.CenterX + previous.Width * previousCosine * .5 <=
                    current.CenterX - current.Width * currentCosine * .5) continue;
                var boundary = (previous.CenterX + previous.ContentWidth * previousCosine * .5 +
                    current.CenterX - current.ContentWidth * currentCosine * .5) * .5;
                var end = Math.Clamp((boundary - previous.CenterX) / previousCosine,
                    -previous.Width * .5 + 1, previous.Width * .5);
                var start = Math.Clamp((boundary - current.CenterX) / currentCosine,
                    -current.Width * .5, current.Width * .5 - 1);
                var previousShift = (end - previous.Width * .5) * .5;
                var currentShift = (start + current.Width * .5) * .5;
                regions[index - 1] = previous with
                {
                    CenterX = previous.CenterX + previousShift * previousCosine,
                    CenterY = previous.CenterY + previousShift * Math.Sin(previous.Angle * Math.PI / 180),
                    Width = previous.Width * .5 + end,
                };
                regions[index] = current with
                {
                    CenterX = current.CenterX + currentShift * currentCosine,
                    CenterY = current.CenterY + currentShift * Math.Sin(current.Angle * Math.PI / 180),
                    Width = current.Width * .5 - start,
                };
            }
        }

        private static List<DetectedTextRegion> ReadingOrder(IReadOnlyList<DetectedTextRegion> regions)
        {
            var angle = regions.Count == 0 ? 0 : regions.MaxBy(value => value.Width).Angle;
            var radians = angle * Math.PI / 180;
            var cosine = Math.Cos(radians);
            var sine = Math.Sin(radians);

            var result = new List<DetectedTextRegion>(regions.Count);
            foreach (var region in regions.OrderBy(Row))
            {
                var index = result.Count;
                while (index > 0 && Math.Abs(Row(result[index - 1]) - Row(region)) <
                       Math.Min(result[index - 1].Height, region.Height) * .5 &&
                       Column(result[index - 1]) > Column(region)) index--;
                result.Insert(index, region);
            }

            return result;

            double Row(DetectedTextRegion value)
            {
                return value.CenterY * cosine - value.CenterX * sine;
            }

            double Column(DetectedTextRegion value)
            {
                return value.CenterX * cosine + value.CenterY * sine;
            }
        }

        private static MagickImage Crop(MagickImage source, DetectedTextRegion region)
        {
            var cosine = Math.Cos(region.Angle * Math.PI / 180);
            var sine = Math.Sin(region.Angle * Math.PI / 180);
            var x = region.CenterX - region.Width * cosine * .5 + region.Height * sine * .5;
            var y = region.CenterY - region.Width * sine * .5 - region.Height * cosine * .5;
            var width = Math.Max(2, (int)Math.Ceiling(region.Width));
            var height = Math.Max(2, (int)Math.Ceiling(region.Height));
            var image = new MagickImage(source);
            try
            {
                image.VirtualPixelMethod = VirtualPixelMethod.White;
                image.Distort(new DistortSettings(DistortMethod.Perspective)
                    {
                        Viewport = new MagickGeometry(0, 0, (uint)width, (uint)height),
                    }, x, y, 0, 0, x + region.Width * cosine, y + region.Width * sine, width - 1, 0,
                    x + region.Width * cosine - region.Height * sine, y + region.Width * sine + region.Height * cosine,
                    width - 1, height - 1, x - region.Height * sine, y + region.Height * cosine, 0, height - 1);
                image.ResetPage();
                return image;
            }
            catch
            {
                image.Dispose();
                throw;
            }
        }

        private static OcrBounds Bounds(DetectedTextRegion region, double start, double end, uint width, uint height)
        {
            var cosine = Math.Cos(region.Angle * Math.PI / 180);
            var sine = Math.Sin(region.Angle * Math.PI / 180);
            var center = (start + end - 1) * region.Width * .5;
            var x = region.CenterX + center * cosine;
            var y = region.CenterY + center * sine;
            var halfWidth = (Math.Abs(cosine) * region.Width * (end - start) + Math.Abs(sine) * region.Height) * .5;
            var halfHeight = (Math.Abs(sine) * region.Width * (end - start) + Math.Abs(cosine) * region.Height) * .5;
            var left = Math.Clamp(x - halfWidth, 0, width);
            var top = Math.Clamp(y - halfHeight, 0, height);
            return new((float)left, (float)top, (float)(Math.Clamp(x + halfWidth, 0, width) - left),
                (float)(Math.Clamp(y + halfHeight, 0, height) - top));
        }
    }
}

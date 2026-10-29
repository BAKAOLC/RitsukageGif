using System.Numerics;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace RitsukageGif.Desktop
{
    internal static partial class ScreenshotDecorationPng
    {
        public static PngRaster Apply(PngRaster source, CaptureDecoration decoration,
            CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(decoration);
            ArgumentNullException.ThrowIfNull(source.Samples);
            ArgumentNullException.ThrowIfNull(source.Metadata);
            token.ThrowIfCancellationRequested();
            var geometry = CaptureDecorationProcessor.Measure(source.Width, source.Height, decoration);
            if (decoration is { CornerRadius: 0, Kind: CaptureDecorationKind.None }) return source;
            var channels = Channels(source.ColorType);
            var bytes = source.Depth == 16 ? 2 : 1;
            if (source.Depth is not (8 or 16) || (source.ColorType == 3 && source.Depth != 8) ||
                source.Width <= 0 || source.Height <= 0 ||
                (long)source.Width * source.Height * channels * bytes != source.Samples.Length)
                throw new ArgumentException("Invalid direct-color PNG raster.", nameof(source));
            CaptureDecorationProcessor.ValidateBudget(source.Width, source.Height, decoration,
                source.Samples.LongLength, 4 * bytes);
            var outputLength = (long)geometry.Width * geometry.Height * 4 * bytes;
            if (outputLength > 512L * 1024 * 1024 ||
                (long)Math.Max(source.Width, geometry.Width) * 16 * 4 > 64L * 1024 * 1024)
                throw new ArgumentException(UiText.Get("RecordingTooLarge"), nameof(source));
            var metadata = OutputMetadata(source, token);
            using var colorSpace = PngColorTransform.CreateSourceSpace(metadata, token);
            using var linearSpace = SKColorSpace.CreateSrgbLinear();
            var palette = source.Metadata.FirstOrDefault(chunk => chunk.Type == "PLTE").Data;
            var transparency = source.Metadata.FirstOrDefault(chunk => chunk.Type == "tRNS").Data;
            var encoded = new Vector4[source.Width];
            var linear = new Vector4[source.Width];
            var unpremultiplied = new Vector4[geometry.Width];
            var converted = new Vector4[geometry.Width];
            var output = new byte[checked((int)outputLength)];
            var sourceY = -1;
            var result = CaptureDecorationProcessor.ApplyRows(source.Width, source.Height, decoration,
                ReadAlpha, ReadSource, WriteOutput, token);
            return source with
            {
                Width = result.Width, Height = result.Height, ColorType = 6, Samples = output, Metadata = metadata,
            };

            void ReadAlpha(int y, Span<float> alpha)
            {
                token.ThrowIfCancellationRequested();
                for (var x = 0; x < source.Width; x++)
                {
                    if ((x & 255) == 0) token.ThrowIfCancellationRequested();
                    alpha[x] = ReadPixel(source, x, y, palette.Span, transparency.Span).W;
                }
            }

            void ReadSource(int y, Span<Vector4> row)
            {
                token.ThrowIfCancellationRequested();
                sourceY = y;
                for (var x = 0; x < source.Width; x++)
                {
                    if ((x & 255) == 0) token.ThrowIfCancellationRequested();
                    encoded[x] = ReadPixel(source, x, y, palette.Span, transparency.Span);
                }

                // ReSharper disable AccessToDisposedClosure
                Transform(encoded, linear, source.Width, colorSpace, linearSpace);
                // ReSharper restore AccessToDisposedClosure
                for (var x = 0; x < source.Width; x++)
                {
                    var value = linear[x];
                    Validate(value);
                    linear[x] = new(value.X * value.W, value.Y * value.W, value.Z * value.W, value.W);
                }

                linear.AsSpan().CopyTo(row);
            }

            void WriteOutput(int y, ReadOnlySpan<Vector4> row)
            {
                token.ThrowIfCancellationRequested();
                for (var x = 0; x < geometry.Width; x++)
                {
                    var value = row[x];
                    Validate(value);
                    unpremultiplied[x] = value.W > 0
                        ? new(value.X / value.W, value.Y / value.W, value.Z / value.W, value.W)
                        : Vector4.Zero;
                }

                // ReSharper disable AccessToDisposedClosure
                Transform(unpremultiplied, converted, geometry.Width, linearSpace, colorSpace);
                // ReSharper restore AccessToDisposedClosure
                for (var x = 0; x < geometry.Width; x++)
                {
                    if ((x & 255) == 0) token.ThrowIfCancellationRequested();
                    var destination = output.AsSpan((y * geometry.Width + x) * 4 * bytes, 4 * bytes);
                    var originalX = x - geometry.ContentOffset.X;
                    // ReSharper disable once CompareOfFloatsByEqualityOperator
                    if (sourceY == y - geometry.ContentOffset.Y && originalX >= 0 && originalX < source.Width &&
                        linear[originalX].W == 1 && linear[originalX] == row[x])
                    {
                        CopyOpaquePixel(source, originalX, sourceY, destination, palette.Span);
                        continue;
                    }

                    Validate(converted[x]);
                    var value = converted[x];
                    WriteComponent(destination, 0, bytes, value.X);
                    WriteComponent(destination, 1, bytes, value.Y);
                    WriteComponent(destination, 2, bytes, value.Z);
                    WriteComponent(destination, 3, bytes, row[x].W);
                }
            }
        }

        private static void Transform(Vector4[] source, Vector4[] destination, int width, SKColorSpace sourceSpace,
            SKColorSpace destinationSpace)
        {
            var input = GCHandle.Alloc(source, GCHandleType.Pinned);
            try
            {
                var output = GCHandle.Alloc(destination, GCHandleType.Pinned);
                try
                {
                    var sourceInfo = new SKImageInfo(width, 1, SKColorType.RgbaF32, SKAlphaType.Unpremul, sourceSpace);
                    var targetInfo = new SKImageInfo(width, 1, SKColorType.RgbaF32, SKAlphaType.Unpremul,
                        destinationSpace);
                    using var pixmap = new SKPixmap(sourceInfo, input.AddrOfPinnedObject(), sourceInfo.RowBytes);
                    if (!pixmap.ReadPixels(targetInfo, output.AddrOfPinnedObject(), targetInfo.RowBytes))
                        throw new InvalidDataException("PNG color transform failed.");
                }
                finally
                {
                    output.Free();
                }
            }
            finally
            {
                input.Free();
            }
        }

        private static void Validate(Vector4 value)
        {
            if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z) ||
                !float.IsFinite(value.W)) throw new InvalidDataException("Invalid PNG color sample.");
        }
    }
}

using System.Buffers;
using System.Numerics;

namespace RitsukageGif.Core
{
    public static partial class CaptureDecorationProcessor
    {
        private const long MaximumWorkingBytes = 512L * 1024 * 1024;

        public static DecorationGeometry Measure(int width, int height, CaptureDecoration decoration)
        {
            ArgumentNullException.ThrowIfNull(decoration);
            decoration.Validate();
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
            if ((long)width * height > 128L * 1024 * 1024)
                throw new ArgumentOutOfRangeException(nameof(width));
            var padding = decoration.Kind == CaptureDecorationKind.None ? 0 : (long)decoration.Strength;
            var outputWidth = width + padding * 2;
            var outputHeight = height + padding * 2;
            if (outputWidth > int.MaxValue || outputHeight > int.MaxValue ||
                outputWidth * outputHeight > 128L * 1024 * 1024)
                throw new ArgumentException(UiText.Get("RecordingTooLarge"), nameof(decoration));
            return new((int)outputWidth, (int)outputHeight, new((int)padding, (int)padding));
        }

        public static DecorationGeometry ValidateBudget(int width, int height, CaptureDecoration decoration,
            long sourceBytes, int outputBytesPerPixel)
        {
            var geometry = Measure(width, height, decoration);
            ArgumentOutOfRangeException.ThrowIfNegative(sourceBytes);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(sourceBytes, MaximumWorkingBytes);
            ArgumentOutOfRangeException.ThrowIfLessThan(outputBytesPerPixel, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(outputBytesPerPixel, 16);
            var shadowBytes = decoration.Kind == CaptureDecorationKind.Shadow
                ? (long)BitOperations.RoundUpToPowerOf2((uint)(geometry.Width * geometry.Height)) * 8
                : 0;
            var rows = ((long)width + geometry.Width) * 128;
            if (sourceBytes + (long)geometry.Width * geometry.Height * outputBytesPerPixel + shadowBytes + rows >
                MaximumWorkingBytes)
                throw new ArgumentException(UiText.Get("RecordingTooLarge"), nameof(decoration));
            return geometry;
        }

        public static DecorationGeometry ApplyRows(int width, int height, CaptureDecoration decoration,
            ReadDecorationAlphaRow readAlpha, ReadDecorationSourceRow readSource,
            WriteDecorationOutputRow writeOutput, CancellationToken token = default)
        {
            return ApplyRowsCore(width, height, decoration, readAlpha, readSource, writeOutput, 1, token);
        }

        private static DecorationGeometry ApplyRowsCore(int width, int height, CaptureDecoration decoration,
            ReadDecorationAlphaRow readAlpha, ReadDecorationSourceRow readSource,
            WriteDecorationOutputRow writeOutput, float whiteScale, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(readAlpha);
            ArgumentNullException.ThrowIfNull(readSource);
            ArgumentNullException.ThrowIfNull(writeOutput);
            token.ThrowIfCancellationRequested();
            var geometry = ValidateBudget(width, height, decoration, 0, 1);
            var corner = Math.Min(decoration.CornerRadius, Math.Min(width, height) / 2d);
            var paint = Paint(decoration.Color, whiteScale);
            float[]? shadow = null;
            var sourceRow = ArrayPool<Vector4>.Shared.Rent(width);
            Vector4[]? outputRow = null;
            try
            {
                outputRow = ArrayPool<Vector4>.Shared.Rent(geometry.Width);
                if (decoration.Kind == CaptureDecorationKind.Shadow)
                    shadow = CreateShadow(width, height, geometry, corner, decoration.Strength, readAlpha, token);
                var border = decoration.Kind == CaptureDecorationKind.Border ? decoration.Strength : 0;
                for (var y = 0; y < geometry.Height; y++)
                {
                    token.ThrowIfCancellationRequested();
                    var sy = y - geometry.ContentOffset.Y;
                    if (sy >= 0 && sy < height)
                    {
                        sourceRow.AsSpan(0, width).Clear();
                        readSource(sy, sourceRow.AsSpan(0, width));
                        foreach (var pixel in sourceRow.AsSpan(0, width))
                            if (!float.IsFinite(pixel.X) || !float.IsFinite(pixel.Y) || !float.IsFinite(pixel.Z) ||
                                !float.IsFinite(pixel.W) || pixel.W is < 0 or > 1)
                                throw new ArgumentException(UiText.Get("InvalidFrameBuffer"), nameof(readSource));
                    }

                    for (var x = 0; x < geometry.Width; x++)
                    {
                        var sx = x - geometry.ContentOffset.X;
                        var coverage = Coverage(sx + .5, sy + .5, 0, 0, width, height, corner);
                        var foreground = sx >= 0 && sx < width && sy >= 0 && sy < height
                            ? sourceRow[sx] * (float)coverage
                            : Vector4.Zero;
                        if (border > 0)
                        {
                            var ring = Math.Max(0, Coverage(sx + .5, sy + .5, -border, -border,
                                width + border, height + border, corner + border) - coverage);
                            var alpha = (float)ring * paint.W;
                            foreground += paint with { W = 1 } * alpha;
                        }

                        if (shadow is not null)
                        {
                            var alpha = shadow[y * geometry.Width + x] * paint.W * (1 - foreground.W);
                            foreground += paint with { W = 1 } * alpha;
                        }

                        outputRow[x] = foreground;
                    }

                    writeOutput(y, outputRow.AsSpan(0, geometry.Width));
                }

                return geometry;
            }
            finally
            {
                if (shadow is not null) ArrayPool<float>.Shared.Return(shadow, true);
                ArrayPool<Vector4>.Shared.Return(sourceRow, true);
                if (outputRow is not null) ArrayPool<Vector4>.Shared.Return(outputRow, true);
            }
        }

        private static double Coverage(double x, double y, double left, double top, double right, double bottom,
            double radius)
        {
            var qx = Math.Abs(x - (left + right) / 2) - (right - left) / 2 + radius;
            var qy = Math.Abs(y - (top + bottom) / 2) - (bottom - top) / 2 + radius;
            var distance = Math.Sqrt(Math.Max(qx, 0) * Math.Max(qx, 0) + Math.Max(qy, 0) * Math.Max(qy, 0)) +
                Math.Min(Math.Max(qx, qy), 0) - radius;
            return Math.Clamp(.5 - distance, 0, 1);
        }

        private static Vector4 Paint(uint color, float whiteScale)
        {
            return new(Linear((color >> 16) & 255) * whiteScale, Linear((color >> 8) & 255) * whiteScale,
                Linear(color & 255) * whiteScale, (color >> 24) / 255f);

            static float Linear(uint value)
            {
                var v = value / 255f;
                return v <= .04045f ? v / 12.92f : MathF.Pow((v + .055f) / 1.055f, 2.4f);
            }
        }
    }
}

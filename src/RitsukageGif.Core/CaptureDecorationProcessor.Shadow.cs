using System.Buffers;

namespace RitsukageGif.Core
{
    public static partial class CaptureDecorationProcessor
    {
        public static DecorationAlphaMask CreateShadowAlpha(int width, int height, CaptureDecoration decoration,
            ReadDecorationAlphaRow? readAlpha = null, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (decoration.Kind != CaptureDecorationKind.Shadow)
                throw new ArgumentException(UiText.Get("InvalidFrameBuffer"), nameof(decoration));
            var geometry = ValidateBudget(width, height, decoration, 0, 1);
            readAlpha ??= static (_, alpha) => alpha.Fill(1);
            var mask = CreateShadow(width, height, geometry,
                Math.Min(decoration.CornerRadius, Math.Min(width, height) / 2d), decoration.Strength, readAlpha, token);
            try
            {
                var output = new byte[checked(geometry.Width * geometry.Height)];
                for (var y = 0; y < geometry.Height; y++)
                {
                    token.ThrowIfCancellationRequested();
                    for (var x = 0; x < geometry.Width; x++)
                    {
                        var index = y * geometry.Width + x;
                        output[index] = (byte)Math.Clamp(MathF.Round(mask[index] * 255), 0, 255);
                    }
                }

                return new(geometry.Width, geometry.Height, geometry.ContentOffset, output);
            }
            finally
            {
                ArrayPool<float>.Shared.Return(mask, true);
            }
        }

        private static float[] CreateShadow(int width, int height, DecorationGeometry geometry, double corner,
            int radius, ReadDecorationAlphaRow readAlpha, CancellationToken token)
        {
            var count = checked(geometry.Width * geometry.Height);
            var mask = ArrayPool<float>.Shared.Rent(count);
            try
            {
                float[]? temporary = null;
                float[]? row = null;
                try
                {
                    temporary = ArrayPool<float>.Shared.Rent(count);
                    row = ArrayPool<float>.Shared.Rent(width);
                    mask.AsSpan(0, count).Clear();
                    for (var y = 0; y < height; y++)
                    {
                        token.ThrowIfCancellationRequested();
                        row.AsSpan(0, width).Clear();
                        readAlpha(y, row.AsSpan(0, width));
                        for (var x = 0; x < width; x++)
                        {
                            if (!float.IsFinite(row[x]) || row[x] is < 0 or > 1)
                                throw new ArgumentException(UiText.Get("InvalidFrameBuffer"), nameof(readAlpha));
                            mask[(y + geometry.ContentOffset.Y) * geometry.Width + x + geometry.ContentOffset.X] =
                                row[x] * (float)Coverage(x + .5, y + .5, 0, 0, width, height, corner);
                        }
                    }

                    Span<int> radii = [radius / 3, (radius + 1) / 3, (radius + 2) / 3];
                    foreach (var support in radii)
                    {
                        if (support == 0) continue;
                        Box(mask, temporary, geometry.Width, geometry.Height, support, true, token);
                        Box(temporary, mask, geometry.Width, geometry.Height, support, false, token);
                    }

                    return mask;
                }
                finally
                {
                    if (temporary is not null) ArrayPool<float>.Shared.Return(temporary, true);
                    if (row is not null) ArrayPool<float>.Shared.Return(row, true);
                }
            }
            catch
            {
                ArrayPool<float>.Shared.Return(mask, true);
                throw;
            }
        }

        private static void Box(float[] source, float[] output, int width, int height, int radius,
            bool horizontal, CancellationToken token)
        {
            var lines = horizontal ? height : width;
            var length = horizontal ? width : height;
            for (var line = 0; line < lines; line++)
            {
                token.ThrowIfCancellationRequested();
                double sum = 0;
                for (var i = 0; i <= Math.Min(radius, length - 1); i++) sum += At(i);
                for (var index = 0; index < length; index++)
                {
                    if ((index & 4095) == 0) token.ThrowIfCancellationRequested();
                    output[horizontal ? line * width + index : index * width + line] =
                        (float)Math.Clamp(sum / (radius * 2d + 1), 0, 1);
                    if (index - radius >= 0) sum -= At(index - radius);
                    if (index + radius + 1 < length) sum += At(index + radius + 1);
                }

                continue;

                float At(int index)
                {
                    return source[horizontal ? line * width + index : index * width + line];
                }
            }
        }
    }
}

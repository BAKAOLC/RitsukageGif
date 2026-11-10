using Avalonia;

namespace RitsukageGif.Desktop
{
    internal static partial class PinnedViewportRasterizer
    {
        private static byte[] Allocate(int width, int height, int bytesPerPixel)
        {
            if (width <= 0 || height <= 0 || (long)width * height > 128L * 1024 * 1024 ||
                (long)width * height * bytesPerPixel > 512L * 1024 * 1024)
                throw new ArgumentOutOfRangeException(nameof(width));
            return new byte[width * height * bytesPerPixel];
        }

        private static void Render<TSource, TTarget>(TSource source, TTarget target, Matrix sourceToTarget,
            int width, int height, double opacity, bool smoothScaling, CancellationToken token)
            where TSource : struct, IPinnedViewportSource
            where TTarget : struct, IPinnedViewportTarget
        {
            token.ThrowIfCancellationRequested();
            if (opacity == 0) return;
            var inverse = sourceToTarget.Invert();
            var footprintX = Math.Abs(inverse.M11) + Math.Abs(inverse.M21);
            var footprintY = Math.Abs(inverse.M12) + Math.Abs(inverse.M22);
            for (var y = 0; y < height; y++)
            {
                token.ThrowIfCancellationRequested();
                var rowX = .5 * inverse.M11 + (y + .5) * inverse.M21 + inverse.M31;
                var rowY = .5 * inverse.M12 + (y + .5) * inverse.M22 + inverse.M32;
                for (var x = 0; x < width; x++)
                {
                    if ((x & 1023) == 0) token.ThrowIfCancellationRequested();
                    var sx = rowX + x * inverse.M11;
                    var sy = rowY + x * inverse.M12;
                    PinnedViewportSample sample;
                    if (smoothScaling)
                        sample = SampleSmooth(source, sx, sy, footprintX, footprintY, token);
                    else
                        sample = sx >= 0 && sy >= 0 && sx < source.Width && sy < source.Height
                            ? source.Read((int)sx, (int)sy)
                            : default;

                    target.Write(y * width + x, sample, opacity);
                }
            }

            token.ThrowIfCancellationRequested();
        }

        private static PinnedViewportSample SampleSmooth<TSource>(TSource source, double x, double y,
            double footprintX, double footprintY, CancellationToken token) where TSource : struct, IPinnedViewportSource
        {
            if (footprintX <= 1 && footprintY <= 1) return SampleBilinear(source, x, y);
            var horizontal = new PinnedViewportKernel(x, footprintX, source.Width);
            var vertical = new PinnedViewportKernel(y, footprintY, source.Height);
            var sum = default(PinnedViewportSample);
            for (var sy = vertical.First; sy <= vertical.Last; sy++)
            {
                token.ThrowIfCancellationRequested();
                var wy = vertical.Weight(sy);
                if (wy == 0) continue;
                for (var sx = horizontal.First; sx <= horizontal.Last; sx++)
                {
                    if ((sx & 4095) == 0) token.ThrowIfCancellationRequested();
                    var weight = wy * horizontal.Weight(sx);
                    if (weight == 0) continue;
                    sum = sum.Add(source.Read(sx, sy), weight);
                }
            }

            return sum;
        }

        private static PinnedViewportSample SampleBilinear<TSource>(TSource source, double x, double y)
            where TSource : struct, IPinnedViewportSource
        {
            if (x <= -.5 || y <= -.5 || x >= source.Width + .5 || y >= source.Height + .5) return default;
            var left = (int)Math.Floor(x - .5);
            var top = (int)Math.Floor(y - .5);
            var fx = x - .5 - left;
            var fy = y - .5 - top;
            var sum = default(PinnedViewportSample);
            if (fx < 1 && fy < 1) sum = sum.Add(Read(source, left, top), (1 - fx) * (1 - fy));
            if (fx > 0 && fy < 1) sum = sum.Add(Read(source, left + 1, top), fx * (1 - fy));
            if (fx < 1 && fy > 0) sum = sum.Add(Read(source, left, top + 1), (1 - fx) * fy);
            if (fx > 0 && fy > 0) sum = sum.Add(Read(source, left + 1, top + 1), fx * fy);
            return sum;
        }

        private static PinnedViewportSample Read<TSource>(TSource source, int x, int y)
            where TSource : struct, IPinnedViewportSource
        {
            return (uint)x < (uint)source.Width && (uint)y < (uint)source.Height ? source.Read(x, y) : default;
        }
    }
}

using System.Buffers;
using ImageMagick;

namespace RitsukageGif.Core
{
    internal sealed class OnnxTextRegionDetector : IDisposable
    {
        private readonly OnnxRuntimeLibrary _runtime;
        private readonly OnnxInferenceSession _session;

        internal OnnxTextRegionDetector(OnnxRuntimeLibrary runtime, string directory)
        {
            _runtime = runtime;
            _session = new(runtime, Path.Combine(directory, "PP-OCRv6_det_tiny.onnx"));
        }

        public void Dispose()
        {
            _session.Dispose();
        }

        internal IReadOnlyList<DetectedTextRegion> Detect(MagickImage source, CancellationToken token)
        {
            var sourceHeight = Math.Max((int)source.Height, (int)Math.Ceiling(source.Width / 8d));
            var ratio = Math.Min(1, 1536d / Math.Max(source.Width, sourceHeight));
            var width = Math.Max(32, (int)Math.Round(source.Width * ratio / 32) * 32);
            var height = Math.Max(32, (int)Math.Round(sourceHeight * ratio / 32) * 32);
            using var resized = source.Clone();
            resized.FilterType = FilterType.Triangle;
            resized.Resize(new MagickGeometry((uint)width,
                    (uint)Math.Max(1, Math.Round(source.Height * height / (double)sourceHeight)))
                { IgnoreAspectRatio = true });
            var paddingPixels = (height - (int)resized.Height) / 2;
            var padding = paddingPixels * sourceHeight / (double)height;
            using var image = new MagickImage(MagickColors.White, (uint)width, (uint)height);
            image.Composite(resized, 0, paddingPixels, CompositeOperator.Over);
            image.Depth = 8;
            var pixels = image.ToByteArray(MagickFormat.Bgr);
            var values = new float[3 * width * height];
            for (var channel = 0; channel < 3; channel++)
            for (var index = 0; index < width * height; index++)
                values[channel * width * height + index] = pixels[index * 3 + channel] / 127.5f - 1;
            using var input = new OnnxTensor(_runtime, values, [1, 3, height, width]);
            using var run = new OnnxRun(_runtime, token);
            var output = _session.Run(run, input);
            try
            {
                var map = output[0];
                return
                [
                    .. Regions(map.Floats, checked((int)map.Dimensions[^1]),
                            checked((int)map.Dimensions[^2]), (int)source.Width, sourceHeight, token)
                        .Select(region => region with { CenterY = region.CenterY - padding }),
                ];
            }
            finally
            {
                foreach (var tensor in output) tensor.Dispose();
            }
        }

        private static DetectedTextRegion[] Regions(ReadOnlySpan<float> values,
            int width, int height, int sourceWidth, int sourceHeight, CancellationToken token)
        {
            var length = checked(width * height);
            var mask = ArrayPool<byte>.Shared.Rent(length);
            var queue = ArrayPool<int>.Shared.Rent(length);
            Array.Clear(mask, 0, length);
            try
            {
                for (var index = 0; index < length; index++)
                {
                    if (values[index] <= .3f) continue;
                    mask[index] = 1;
                    var right = index % width + 1 < width;
                    var below = index + width < length;
                    if (right) mask[index + 1] = 1;
                    if (below) mask[index + width] = 1;
                    if (right && below) mask[index + width + 1] = 1;
                }

                var result = new List<DetectedTextRegion>();
                for (var seed = 0; seed < length; seed++)
                {
                    if ((seed & 4095) == 0) token.ThrowIfCancellationRequested();
                    if (mask[seed] != 1) continue;
                    var count = 1;
                    queue[0] = seed;
                    mask[seed] = 2;
                    double sx = 0, sy = 0, xx = 0, yy = 0, xy = 0;
                    for (var head = 0; head < count; head++)
                    {
                        if ((head & 4095) == 0) token.ThrowIfCancellationRequested();
                        var index = queue[head];
                        var x = index % width;
                        var y = index / width;
                        sx += x;
                        sy += y;
                        xx += (double)x * x;
                        yy += (double)y * y;
                        xy += (double)x * y;
                        for (var dy = -1; dy <= 1; dy++)
                        for (var dx = -1; dx <= 1; dx++)
                        {
                            var nx = x + dx;
                            var ny = y + dy;
                            if ((uint)nx >= (uint)width || (uint)ny >= (uint)height) continue;
                            var next = ny * width + nx;
                            if (mask[next] != 1) continue;
                            mask[next] = 2;
                            queue[count++] = next;
                        }
                    }

                    if (count < 9) continue;
                    var angle = Math.Atan2(2 * (xy - sx * sy / count),
                        xx - sx * sx / count - yy + sy * sy / count) * .5;
                    var cosine = Math.Cos(angle);
                    var sine = Math.Sin(angle);
                    var x0 = double.PositiveInfinity;
                    var y0 = double.PositiveInfinity;
                    var x1 = double.NegativeInfinity;
                    var y1 = double.NegativeInfinity;
                    double score = 0;
                    for (var index = 0; index < count; index++)
                    {
                        var pixel = queue[index];
                        var x = pixel % width;
                        var y = pixel / width;
                        var u = x * cosine + y * sine;
                        var v = y * cosine - x * sine;
                        x0 = Math.Min(x0, u);
                        y0 = Math.Min(y0, v);
                        x1 = Math.Max(x1, u);
                        y1 = Math.Max(y1, v);
                        score += values[pixel];
                    }

                    score /= count;
                    var regionWidth = x1 - x0 + 1;
                    var regionHeight = y1 - y0 + 1;
                    if (score < .5 || Math.Min(regionWidth, regionHeight) < 3) continue;
                    var margin = regionWidth * regionHeight * 1.6 / (2 * (regionWidth + regionHeight));
                    var centerU = (x0 + x1) * .5;
                    var centerV = (y0 + y1) * .5;
                    var scaleX = sourceWidth / (double)width;
                    var scaleY = sourceHeight / (double)height;
                    result.Add(new((centerU * cosine - centerV * sine) * scaleX,
                        (centerU * sine + centerV * cosine) * scaleY,
                        (regionWidth + 2 * margin) *
                        Math.Sqrt(Math.Pow(cosine * scaleX, 2) + Math.Pow(sine * scaleY, 2)),
                        (regionHeight + 2 * margin) *
                        Math.Sqrt(Math.Pow(sine * scaleX, 2) + Math.Pow(cosine * scaleY, 2)),
                        Math.Atan2(sine * scaleY, cosine * scaleX) * 180 / Math.PI,
                        regionWidth * Math.Sqrt(Math.Pow(cosine * scaleX, 2) + Math.Pow(sine * scaleY, 2))));
                }

                return [.. result];
            }
            finally
            {
                ArrayPool<int>.Shared.Return(queue);
                ArrayPool<byte>.Shared.Return(mask);
            }
        }
    }
}

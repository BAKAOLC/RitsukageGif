using ImageMagick;

namespace RitsukageGif.Core
{
    internal sealed class OnnxFormulaRegionDetector : IDisposable
    {
        private readonly int _formulaClass;
        private readonly OnnxRuntimeLibrary _runtime;
        private readonly OnnxInferenceSession _session;

        internal OnnxFormulaRegionDetector(OnnxRuntimeLibrary runtime, string directory)
        {
            _runtime = runtime;
            _session = new(runtime, Path.Combine(directory, "pp_doclayout_s.onnx"));
            try
            {
                var labels = (_session.Metadata("character") ?? "").Split('\n',
                    StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                _formulaClass = Array.IndexOf(labels, "formula");
                if (_formulaClass < 0) throw new InvalidDataException("The layout model contains no formula category.");
            }
            catch
            {
                _session.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            _session.Dispose();
        }

        internal IReadOnlyList<DetectedFormulaRegion> Detect(MagickImage source, CancellationToken token)
        {
            int width = (int)source.Width, height = (int)source.Height;
            using var image = source.Clone();
            image.FilterType = FilterType.Cubic;
            image.Resize(new MagickGeometry(480, 480) { IgnoreAspectRatio = true });
            image.Depth = 8;
            var bgr = image.ToByteArray(MagickFormat.Bgr);
            var input = new float[3 * 480 * 480];
            ReadOnlySpan<float> mean = [.485f, .456f, .406f], deviation = [.229f, .224f, .225f];
            for (var channel = 0; channel < 3; channel++)
            for (var index = 0; index < 480 * 480; index++)
                input[channel * 480 * 480 + index] =
                    (bgr[index * 3 + channel] / 255f - mean[channel]) / deviation[channel];
            using var pixels = new OnnxTensor(_runtime, input, [1, 3, 480, 480]);
            using var scale = new OnnxTensor(_runtime, [480f / height, 480f / width], [1, 2]);
            using var run = new OnnxRun(_runtime, token);
            var outputs = _session.Run(run, pixels, scale);
            try
            {
                var values = outputs[0].Floats;
                var regions = new List<DetectedFormulaRegion>();
                for (var index = 0; index < values.Length; index += 6)
                {
                    token.ThrowIfCancellationRequested();
                    if ((int)values[index] != _formulaClass || values[index + 1] < .4f) continue;
                    var left = Math.Clamp((int)Math.Floor(values[index + 2]) - 8, 0, width);
                    var top = Math.Clamp((int)Math.Floor(values[index + 3]) - 8, 0, height);
                    var right = Math.Clamp((int)Math.Ceiling(values[index + 4]) + 8, 0, width);
                    var bottom = Math.Clamp((int)Math.Ceiling(values[index + 5]) + 8, 0, height);
                    if (right > left && bottom > top)
                        regions.Add(new(left, top, right - left, bottom - top, values[index + 1]));
                }

                return [.. regions.OrderBy(region => region.Y).ThenBy(region => region.X)];
            }
            finally
            {
                foreach (var output in outputs) output.Dispose();
            }
        }
    }
}

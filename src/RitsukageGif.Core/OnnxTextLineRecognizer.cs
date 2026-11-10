using System.Text;
using ImageMagick;

namespace RitsukageGif.Core
{
    internal sealed class OnnxTextLineRecognizer : IDisposable
    {
        private readonly string[] _characters;
        private readonly OnnxRuntimeLibrary _runtime;
        private readonly OnnxInferenceSession _session;

        internal OnnxTextLineRecognizer(OnnxRuntimeLibrary runtime, string directory)
        {
            _runtime = runtime;
            _session = new(runtime, Path.Combine(directory, "PP-OCRv6_rec_small.onnx"));
            try
            {
                _characters =
                [
                    "",
                    .. (_session.Metadata("character") ??
                        throw new InvalidDataException("The text model has no character vocabulary."))
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries),
                    " ",
                ];
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

        internal RecognizedTextLine[] Recognize(IReadOnlyList<MagickImage> images, CancellationToken token)
        {
            var result = new RecognizedTextLine[images.Count];
            using var run = new OnnxRun(_runtime, token);
            for (var first = 0; first < images.Count;)
            {
                token.ThrowIfCancellationRequested();
                var widths = new int[Math.Min(8, images.Count - first)];
                for (var index = 0; index < widths.Length; index++)
                    widths[index] = Math.Clamp((int)Math.Ceiling(images[first + index].Width * 48d /
                                                                 images[first + index].Height), 1, 16384);
                var width = Math.Max(32, widths.Max());
                var count = Math.Min(widths.Length, Math.Max(1, 16384 / width));
                width = 32;
                for (var index = 0; index < count; index++) width = Math.Max(width, widths[index]);
                var data = new float[count * 3 * 48 * width];
                for (var index = 0; index < count; index++)
                {
                    using var image = images[first + index].Clone();
                    image.FilterType = FilterType.Triangle;
                    image.Resize(new MagickGeometry((uint)widths[index], 48) { IgnoreAspectRatio = true });
                    image.Depth = 8;
                    var pixels = image.ToByteArray(MagickFormat.Bgr);
                    for (var channel = 0; channel < 3; channel++)
                    for (var y = 0; y < 48; y++)
                    for (var x = 0; x < widths[index]; x++)
                        data[(index * 3 + channel) * 48 * width + y * width + x] =
                            pixels[(y * widths[index] + x) * 3 + channel] / 127.5f - 1;
                }

                using var input = new OnnxTensor(_runtime, data, [count, 3, 48, width]);
                var output = _session.Run(run, input);
                try
                {
                    var values = output[0].Floats;
                    var classes = checked((int)output[0].Dimensions[^1]);
                    var steps = checked((int)output[0].Dimensions[^2]);
                    if (_characters.Length != classes)
                        throw new InvalidDataException("The text model vocabulary does not match its output.");
                    for (var index = 0; index < count; index++)
                    {
                        var units = new List<RecognizedTextUnit>();
                        var previous = -1;
                        for (var step = 0; step < steps; step++)
                        {
                            var offset = (index * steps + step) * classes;
                            var best = 0;
                            for (var character = 1; character < classes; character++)
                                if (values[offset + character] > values[offset + best])
                                    best = character;
                            if (best != 0)
                            {
                                var start = Math.Clamp(step * width / (double)(steps * widths[index]), 0, 1);
                                var end = Math.Clamp((step + 1) * width / (double)(steps * widths[index]), 0, 1);
                                if (best != previous)
                                    units.Add(new(_characters[best], start, end, values[offset + best]));
                                else
                                    units[^1] = units[^1] with
                                    {
                                        End = end, Confidence = Math.Max(units[^1].Confidence, values[offset + best]),
                                    };
                            }

                            previous = best;
                        }

                        var previousCenter = 0d;
                        for (var unitIndex = 0; unitIndex < units.Count; unitIndex++)
                        {
                            var unit = units[unitIndex];
                            var center = (unit.Start + unit.End) * .5;
                            var nextCenter = unitIndex + 1 < units.Count
                                ? (units[unitIndex + 1].Start + units[unitIndex + 1].End) * .5
                                : center;
                            units[unitIndex] = unit with
                            {
                                Start = unitIndex == 0 ? 0 : (previousCenter + center) * .5,
                                End = unitIndex + 1 == units.Count ? 1 : (center + nextCenter) * .5,
                            };
                            previousCenter = center;
                        }

                        var text = new StringBuilder();
                        foreach (var unit in units) text.Append(unit.Text);
                        result[first + index] = new(text.ToString(),
                            units.Count == 0 ? 0 : units.Average(unit => unit.Confidence), units);
                    }
                }
                finally
                {
                    foreach (var tensor in output) tensor.Dispose();
                }

                first += count;
            }

            return result;
        }
    }
}

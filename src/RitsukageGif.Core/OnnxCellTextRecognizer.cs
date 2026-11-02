using System.Text;

namespace RitsukageGif.Core
{
    internal sealed class OnnxCellTextRecognizer : IDisposable
    {
        private readonly string[] _characters;
        private readonly OnnxRuntimeLibrary _runtime;
        private readonly OnnxInferenceSession _session;

        internal OnnxCellTextRecognizer(OnnxRuntimeLibrary runtime, string directory)
        {
            _runtime = runtime;
            _session = new(runtime, Path.Combine(directory, "PP-OCRv6_rec_tiny.onnx"));
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

        internal void Recognize(GrayImage image, TableStructureCell[] cells, CancellationToken token)
        {
            var items = cells.SelectMany((cell, index) => GrayInkLayout.Lines(image, cell).Select(line =>
                (Cell: index,
                    Image: line.Resize(Math.Clamp((int)Math.Ceiling(line.Width * 48d / line.Height), 32, 4096), 48,
                        true)))).ToArray();
            using var run = new OnnxRun(_runtime, token);
            for (var first = 0; first < items.Length;)
            {
                token.ThrowIfCancellationRequested();
                var count = Math.Min(8, items.Length - first);
                var width = BatchWidth(items, first, count);
                count = Math.Max(1, Math.Min(count, 4096 / width));
                width = BatchWidth(items, first, count);
                var data = new float[count * 3 * 48 * width];
                for (var batch = 0; batch < count; batch++)
                {
                    var line = items[first + batch].Image;
                    for (var channel = 0; channel < 3; channel++)
                    for (var y = 0; y < 48; y++)
                    for (var x = 0; x < line.Width; x++)
                        data[(batch * 3 + channel) * 48 * width + y * width + x] =
                            (line.Pixels[y * line.Width + x] / 255f - .5f) / .5f;
                }

                using var input = new OnnxTensor(_runtime, data, [count, 3, 48, width]);
                var outputs = _session.Run(run, input);
                try
                {
                    var values = outputs[0].Floats;
                    var classes = checked((int)outputs[0].Dimensions[^1]);
                    var steps = checked((int)outputs[0].Dimensions[^2]);
                    if (_characters.Length != classes)
                        throw new InvalidDataException("The text model vocabulary does not match its output.");
                    for (var batch = 0; batch < count; batch++)
                    {
                        var result = new StringBuilder();
                        var previous = -1;
                        for (var step = 0; step < steps; step++)
                        {
                            var offset = (batch * steps + step) * classes;
                            var best = 0;
                            for (var index = 1; index < classes; index++)
                                if (values[offset + index] > values[offset + best])
                                    best = index;
                            if (best != 0 && best != previous) result.Append(_characters[best]);
                            previous = best;
                        }

                        var cell = cells[items[first + batch].Cell];
                        if (cell.Text.Length != 0) cell.Text += "\n";
                        cell.Text += result;
                    }
                }
                finally
                {
                    foreach (var output in outputs) output.Dispose();
                }

                first += count;
            }
        }

        private static int BatchWidth((int Cell, GrayImage Image)[] items, int first, int count)
        {
            var width = 0;
            for (var index = first; index < first + count; index++) width = Math.Max(width, items[index].Image.Width);
            return width;
        }
    }
}

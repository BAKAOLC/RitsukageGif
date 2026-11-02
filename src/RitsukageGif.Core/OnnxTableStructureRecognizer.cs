using System.Text;
using System.Xml.Linq;
using ImageMagick;

namespace RitsukageGif.Core
{
    internal sealed class OnnxTableStructureRecognizer : IDisposable
    {
        private readonly OnnxRuntimeLibrary _runtime;
        private readonly OnnxInferenceSession _session;
        private readonly string[] _tokens;

        internal OnnxTableStructureRecognizer(OnnxRuntimeLibrary runtime, string directory)
        {
            _runtime = runtime;
            _session = new(runtime, Path.Combine(directory, "slanet-plus.onnx"));
            try
            {
                _tokens =
                [
                    "sos",
                    .. (_session.Metadata("character") ??
                        throw new InvalidDataException("The table model has no structure vocabulary."))
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries),
                    "eos",
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

        internal TableStructureCell[] Recognize(MagickImage source, GrayImage gray, CancellationToken token)
        {
            using var image = source.Clone();
            image.AutoOrient();
            image.BackgroundColor = MagickColors.White;
            image.Alpha(AlphaAction.Remove);
            image.ColorSpace = ColorSpace.sRGB;
            var originalWidth = checked((int)image.Width);
            var originalHeight = checked((int)image.Height);
            var ratio = 488d / Math.Max(originalWidth, originalHeight);
            var width = Math.Max(1, (int)(originalWidth * ratio));
            var height = Math.Max(1, (int)(originalHeight * ratio));
            image.FilterType = FilterType.Triangle;
            image.Resize(new MagickGeometry((uint)width, (uint)height) { IgnoreAspectRatio = true });
            image.Depth = 8;
            var bgr = image.ToByteArray(MagickFormat.Bgr);
            var data = new float[3 * 488 * 488];
            ReadOnlySpan<float> mean = [.485f, .456f, .406f];
            ReadOnlySpan<float> deviation = [.229f, .224f, .225f];
            for (var channel = 0; channel < 3; channel++)
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                data[channel * 488 * 488 + y * 488 + x] =
                    (bgr[(y * width + x) * 3 + channel] / 255f - mean[channel]) / deviation[channel];
            using var input = new OnnxTensor(_runtime, data, [1, 3, 488, 488]);
            using var run = new OnnxRun(_runtime, token);
            var outputs = _session.Run(run, input);
            try
            {
                var boxes = outputs[0].Floats;
                var probabilities = outputs[1].Floats;
                var classes = checked((int)outputs[1].Dimensions[^1]);
                if (_tokens.Length != classes)
                    throw new InvalidDataException("The table model vocabulary does not match its output.");
                var count = checked((int)outputs[1].Dimensions[^2]);
                var structure = new StringBuilder("<table>");
                var rectangles = new List<(double X, double Y, double Width, double Height)>();
                var scale = Math.Max(originalWidth, originalHeight);
                for (var index = 0; index < count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    var values = probabilities.Slice(index * classes, classes);
                    var best = 0;
                    for (var candidate = 1; candidate < classes; candidate++)
                        if (values[candidate] > values[best])
                            best = candidate;
                    if (best == classes - 1) break;
                    if (best == 0) continue;
                    var text = _tokens[best];
                    structure.Append(text);
                    if (text is not ("<td>" or "<td" or "<td></td>")) continue;
                    var corners = boxes.Slice(index * 8, 8);
                    double left = originalWidth, top = originalHeight, right = 0, bottom = 0;
                    for (var corner = 0; corner < 8; corner += 2)
                    {
                        var x = Math.Clamp(corners[corner] * scale, 0, originalWidth);
                        var y = Math.Clamp(corners[corner + 1] * scale, 0, originalHeight);
                        left = Math.Min(left, x);
                        top = Math.Min(top, y);
                        right = Math.Max(right, x);
                        bottom = Math.Max(bottom, y);
                    }

                    rectangles.Add((left, top, Math.Max(0, right - left), Math.Max(0, bottom - top)));
                }

                structure.Append("</table>");
                var table = XElement.Parse(structure.ToString());
                var rows = table.Descendants("tr").ToArray();
                var used = new HashSet<(int Row, int Column)>();
                var cells = new List<TableStructureCell>();
                for (var row = 0; row < rows.Length; row++)
                {
                    var column = 0;
                    foreach (var cell in rows[row].Elements("td"))
                    {
                        while (used.Contains((row, column))) column++;
                        var rowSpan = (int?)cell.Attribute("rowspan") ?? 1;
                        var columnSpan = (int?)cell.Attribute("colspan") ?? 1;
                        var bounds = rectangles[cells.Count];
                        cells.Add(
                            new(row, column, rowSpan, columnSpan, bounds.X, bounds.Y, bounds.Width, bounds.Height));
                        for (var y = row; y < row + rowSpan; y++)
                        for (var x = column; x < column + columnSpan; x++)
                            used.Add((y, x));
                        column += columnSpan;
                    }
                }

                if (cells.Count == 0 || cells.Count != rectangles.Count)
                    throw new InvalidDataException("The image did not produce a consistent table structure.");
                return TableGridRefinement.Refine(gray, [.. cells]);
            }
            finally
            {
                foreach (var output in outputs) output.Dispose();
            }
        }
    }
}

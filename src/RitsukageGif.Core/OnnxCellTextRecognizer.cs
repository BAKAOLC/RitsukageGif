using ImageMagick;

namespace RitsukageGif.Core
{
    internal sealed class OnnxCellTextRecognizer : IDisposable
    {
        private readonly OnnxTextLineRecognizer _recognizer;

        internal OnnxCellTextRecognizer(OnnxRuntimeLibrary runtime, string directory)
        {
            _recognizer = new(runtime, directory);
        }

        public void Dispose()
        {
            _recognizer.Dispose();
        }

        internal void Recognize(GrayImage image, TableStructureCell[] cells, CancellationToken token)
        {
            var items = cells.SelectMany((cell, index) => GrayInkLayout.Lines(image, cell)
                .Select(line => (Cell: index, Image: line))).ToArray();
            var images = new List<MagickImage>(items.Length);
            try
            {
                foreach (var item in items)
                {
                    token.ThrowIfCancellationRequested();
                    images.Add(new(item.Image.Pixels,
                        new PixelReadSettings((uint)item.Image.Width, (uint)item.Image.Height, StorageType.Char, "I")));
                }

                var recognized = _recognizer.Recognize(images, token);
                for (var index = 0; index < recognized.Length; index++)
                {
                    var cell = cells[items[index].Cell];
                    if (cell.Text.Length != 0) cell.Text += "\n";
                    cell.Text += recognized[index].Text;
                }
            }
            finally
            {
                foreach (var value in images) value.Dispose();
            }
        }
    }
}

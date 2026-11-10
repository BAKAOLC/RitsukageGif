using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class TableRecognitionGrid
    {
        private TableContentAlignment _contentAlignment;

        internal TableContentAlignment ContentAlignment
        {
            set
            {
                if (_contentAlignment == value) return;
                _contentAlignment = value;
                _editor.TextAlignment = value switch
                {
                    TableContentAlignment.Center => TextAlignment.Center,
                    TableContentAlignment.Right => TextAlignment.Right,
                    _ => TextAlignment.Left,
                };
                InvalidateTable();
            }
        }

        private double TextOffset(double width, double textWidth)
        {
            return _contentAlignment switch
            {
                TableContentAlignment.Center => Math.Max(5, (width - textWidth) / 2),
                TableContentAlignment.Right => Math.Max(5, width - textWidth - 5),
                _ => 5,
            };
        }
    }
}

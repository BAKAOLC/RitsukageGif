using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace RitsukageGif.Desktop
{
    internal sealed partial class TablePrintRenderer
    {
        private static RenderTargetBitmap Draw(TablePrintDocument document, TablePrintPage page,
            DocumentOutputOptions options, CancellationToken token)
        {
            var pixelWidth = checked((int)Math.Ceiling(document.Width * options.Dpi / 96));
            var pixelHeight = checked((int)Math.Ceiling(document.Height * options.Dpi / 96));
            var bitmap = new RenderTargetBitmap(new(pixelWidth, pixelHeight), new(options.Dpi, options.Dpi));
            try
            {
                var background = new SolidColorBrush(Color.Parse(options.Background));
                var border = new Pen(Brushes.Gray, .65);
                using (var context = bitmap.CreateDrawingContext())
                {
                    context.FillRectangle(background,
                        new(0, 0, pixelWidth * 96 / options.Dpi, pixelHeight * 96 / options.Dpi));
                    using var translate = context.PushTransform(Matrix.CreateTranslation(document.Left, document.Top));
                    using var clip = context.PushClip(new(0, 0, document.ContentWidth, document.ContentHeight));
                    using var scale = context.PushTransform(Matrix.CreateScale(document.Scale, document.Scale));
                    var firstColumn = document.PanelStarts[page.Panel];
                    var lastColumn = document.PanelStarts[page.Panel + 1];
                    var panelX = document.Columns[firstColumn];
                    var panelWidth = document.Columns[lastColumn] - panelX;
                    var pageStart = page.StartBand * document.Pitch;
                    var pageEnd = page.EndBand * document.Pitch;
                    var pageHeight = pageEnd - pageStart;
                    for (var column = firstColumn;
                         document.OutputStyle.Borders == TableBorderStyle.All && column <= lastColumn;
                         column++)
                    {
                        var x = document.Columns[column] - panelX;
                        context.DrawLine(border, new(x, 0), new(x, pageHeight));
                    }

                    foreach (var band in document.RowBands)
                        if (document.OutputStyle.Borders == TableBorderStyle.All && band >= page.StartBand &&
                            band <= page.EndBand)
                        {
                            var y = band * document.Pitch - pageStart;
                            context.DrawLine(border, new(0, y), new(panelWidth, y));
                        }

                    var index = 0;
                    foreach (var (source, panel, cellX, lines) in document.Cells)
                    {
                        if ((index++ & 127) == 0) token.ThrowIfCancellationRequested();
                        if (source.Column >= lastColumn || source.Column + source.ColumnSpan <= firstColumn) continue;
                        var firstBand = document.RowBands[source.Row];
                        var lastBand = document.RowBands[source.Row + source.RowSpan];
                        if (firstBand >= page.EndBand || lastBand <= page.StartBand) continue;
                        var x = Math.Max(document.Columns[source.Column], panelX) - panelX;
                        var right = Math.Min(document.Columns[source.Column + source.ColumnSpan], panelX + panelWidth) -
                                    panelX;
                        var y = Math.Max(firstBand * document.Pitch, pageStart) - pageStart;
                        var bottom = Math.Min(lastBand * document.Pitch, pageEnd) - pageStart;
                        var rect = new Rect(x, y, right - x, bottom - y);
                        context.DrawRectangle(background,
                            document.OutputStyle.Borders == TableBorderStyle.All ? border : null, rect);
                        if (panel != page.Panel) continue;
                        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
                        {
                            var band = firstBand + lineIndex;
                            if (band < page.StartBand || band >= page.EndBand) continue;
                            if ((lineIndex & 127) == 0) token.ThrowIfCancellationRequested();
                            var line = lines[lineIndex];
                            var offset = document.OutputStyle.Alignment switch
                            {
                                TableContentAlignment.Center => Math.Max(4,
                                    (rect.Width - line.WidthIncludingTrailingWhitespace) / 2),
                                TableContentAlignment.Right => Math.Max(4,
                                    rect.Width - line.WidthIncludingTrailingWhitespace - 4),
                                _ => 4,
                            };
                            line.Draw(context, new(cellX - panelX + offset, band * document.Pitch - pageStart + 3));
                        }
                    }

                    if (document.OutputStyle.Borders != TableBorderStyle.None)
                        context.DrawRectangle(null, border, new(0, 0, panelWidth, pageHeight));
                }

                token.ThrowIfCancellationRequested();
                return bitmap;
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }
        }

        private static RasterPrintPage Encode(RenderTargetBitmap bitmap, double dpi, double widthPoints,
            double heightPoints,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using var stream = new MemoryStream();
            bitmap.Save(stream, PngBitmapEncoderOptions.Default);
            token.ThrowIfCancellationRequested();
            return new(stream.ToArray(), dpi, widthPoints, heightPoints);
        }
    }
}

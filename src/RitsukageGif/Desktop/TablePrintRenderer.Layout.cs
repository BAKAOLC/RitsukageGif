using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace RitsukageGif.Desktop
{
    internal sealed partial class TablePrintRenderer
    {
        private static TablePrintDocument Measure(TableRecognitionResult table, DocumentOutputOptions options,
            CancellationToken token)
        {
            Validate(table, token);
            if (!Enum.IsDefined(options.PageMode)) throw new ArgumentOutOfRangeException(nameof(options));
            var geometry = PdfDocumentLayout.Create(Math.Max(1, table.Width), Math.Max(1, table.Height),
                options with { PageMode = DocumentPageMode.Fit })[0];
            const double dipPerPoint = 96d / 72;
            var width = geometry.WidthPoints * dipPerPoint;
            var height = geometry.HeightPoints * dipPerPoint;
            var left = options.Margins.Left * 96 / 25.4;
            var top = options.Margins.Top * 96 / 25.4;
            var contentWidth = width - left - options.Margins.Right * 96 / 25.4;
            var contentHeight = height - top - options.Margins.Bottom * 96 / 25.4;
            if (contentWidth < 40 || contentHeight < 40)
                throw new ArgumentException("Table print margins leave insufficient text space.", nameof(options));
            var pixels = Math.Ceiling(width * options.Dpi / 96) * Math.Ceiling(height * options.Dpi / 96);
            if (!double.IsFinite(pixels) || pixels > 32_000_000)
                throw new InvalidOperationException("Table print page exceeds the 32 megapixel budget.");
            var columnWidths = new double[table.Columns];
            var weights = new double[table.Columns];
            Array.Fill(weights, 1);
            foreach (var cell in table.Cells)
                if (float.IsFinite(cell.Bounds.Width) && cell.Bounds.Width > 0)
                    for (var column = cell.Column; column < cell.Column + cell.ColumnSpan; column++)
                        weights[column] = Math.Max(weights[column], cell.Bounds.Width / cell.ColumnSpan);
            var minimum = Math.Min(64, contentWidth);
            var extra = Math.Max(0, contentWidth - table.Columns * minimum);
            var weightSum = weights.Sum();
            for (var column = 0; column < table.Columns; column++)
                columnWidths[column] = minimum + extra * weights[column] / weightSum;
            var columns = new double[table.Columns + 1];
            for (var column = 0; column < table.Columns; column++)
                columns[column + 1] = columns[column] + columnWidths[column];
            var panelStarts = new List<int> { 0 };
            if (options.PageMode != DocumentPageMode.Fit)
                for (var column = 1; column < table.Columns; column++)
                    if (columns[column + 1] - columns[panelStarts[^1]] > contentWidth + .001)
                        panelStarts.Add(column);
            panelStarts.Add(table.Columns);
            var cells = new List<TablePrintCell>();
            var pitch = 20d;
            var totalLines = 0;
            try
            {
                foreach (var cell in table.Cells)
                {
                    token.ThrowIfCancellationRequested();
                    var panel = panelStarts.FindLastIndex(start => start <= cell.Column);
                    var end = Math.Min(cell.Column + cell.ColumnSpan, panelStarts[panel + 1]);
                    var cellWidth = columns[end] - columns[cell.Column];
                    var lines = MeasureText(cell.Text, cellWidth - 8, token);
                    cells.Add(new(cell, panel, columns[cell.Column], lines));
                    totalLines += lines.Count;
                    if (totalLines > 100_000)
                        throw new InvalidOperationException("Table print exceeds the 100000 text line budget.");
                    // ReSharper disable once ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator
                    foreach (var line in lines) pitch = Math.Max(pitch, line.Height + 6);
                }

                var rowHeights = Enumerable.Repeat(1, table.Rows).ToArray();
                foreach (var (source, _, _, lines) in cells.OrderBy(cell => cell.Source.RowSpan))
                {
                    var available = 0;
                    for (var row = source.Row; row < source.Row + source.RowSpan; row++) available += rowHeights[row];
                    var missing = Math.Max(0, lines.Count - available);
                    var each = missing / source.RowSpan;
                    var remainder = missing % source.RowSpan;
                    for (var row = source.Row; row < source.Row + source.RowSpan; row++)
                        rowHeights[row] += each + (row - source.Row < remainder ? 1 : 0);
                }

                var rows = new int[table.Rows + 1];
                for (var row = 0; row < table.Rows; row++) rows[row + 1] = checked(rows[row] + rowHeights[row]);
                var scale = options.PageMode == DocumentPageMode.Fit
                    ? Math.Min(1, Math.Min(contentWidth / columns[^1], contentHeight / (rows[^1] * pitch)))
                    : 1;
                var pages = Paginate(table, options, rows, panelStarts.Count - 1, contentHeight, pitch, token);
                return new(width, height, left, top, contentWidth, contentHeight, pitch, scale, columns,
                    [.. panelStarts], rows, cells, pages);
            }
            catch
            {
                foreach (var cell in cells)
                foreach (var line in cell.Lines)
                    line.Dispose();
                throw;
            }
        }

        private static List<TextLine> MeasureText(string text, double width, CancellationToken token)
        {
            var paragraph = new PinnedRichParagraph(0, text.Length, new(), [new(0, text, new() { FontSize = 13 })]);
            var source = new PinnedRichTextSource(paragraph, Typeface.Default, Brushes.Black, width,
                _ => default, (_, _, _) => { });
            var properties = new GenericTextParagraphProperties(source.Properties(new() { FontSize = 13 }),
                TextAlignment.Left, TextWrapping.Wrap);
            var lines = new List<TextLine>();
            try
            {
                var position = 0;
                TextLineBreak? previous = null;
                while (position < text.Length)
                {
                    token.ThrowIfCancellationRequested();
                    var line = TextFormatter.Current.FormatLine(source, position, width, properties, previous) ??
                               throw new InvalidOperationException("Table formatter returned no line.");
                    lines.Add(line);
                    if (line.Length <= 0) throw new InvalidOperationException("Table text formatter made no progress.");
                    position = checked(position + line.Length);
                    previous = line.TextLineBreak;
                    if (line.Height > 200 || line.Width > width + 2)
                        throw new InvalidOperationException(
                            $"Table text cannot fit a readable column. Select a wider paper size. Width={line.Width} Limit={width} Height={line.Height}");
                    if (lines.Count > 100_000)
                        throw new InvalidOperationException("Table cell exceeds the text line budget.");
                }

                return lines;
            }
            catch
            {
                foreach (var line in lines) line.Dispose();
                throw;
            }
        }

        private static void Validate(TableRecognitionResult table, CancellationToken token)
        {
            if (table.Rows is < 1 or > 100_000 || table.Columns is < 1 or > 4096 ||
                (long)table.Rows * table.Columns > 200_000)
                throw new ArgumentException("Table print exceeds the 200000 grid position budget.", nameof(table));
            ArgumentNullException.ThrowIfNull(table.Cells);
            var occupied = new bool[table.Rows * table.Columns];
            long characters = 0;
            foreach (var cell in table.Cells)
            {
                token.ThrowIfCancellationRequested();
                if (cell.Row < 0 || cell.Column < 0 || cell.RowSpan < 1 ||
                    cell.ColumnSpan < 1 ||
                    cell.RowSpan > table.Rows - cell.Row || cell.ColumnSpan > table.Columns - cell.Column)
                    throw new ArgumentException("Table contains an invalid cell span.", nameof(table));
                characters += cell.Text.Length;
                if (characters > 4_000_000) throw new InvalidOperationException("Table print exceeds the text budget.");
                for (var row = cell.Row; row < cell.Row + cell.RowSpan; row++)
                for (var column = cell.Column; column < cell.Column + cell.ColumnSpan; column++)
                {
                    var index = row * table.Columns + column;
                    if (occupied[index])
                        throw new ArgumentException("Table contains overlapping cells.", nameof(table));
                    occupied[index] = true;
                }
            }
        }
    }
}

using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedRichLayout
    {
        private double LayoutTable(PinnedRichTable table, PinnedRichBox box, double x, double y, double width)
        {
            if (table.Columns == 0 || table.Rows == 0) return 0;
            var gap = table.CellSpacing;
            var available = Math.Max(table.Columns, width - gap * (table.Columns + 1));
            var minimum = Enumerable.Repeat(1d, table.Columns).ToArray();
            var preferred = Enumerable.Repeat(1d, table.Columns).ToArray();
            foreach (var cell in table.Cells.OrderBy(cell => cell.ColumnSpan))
            {
                _relativeWidth |= _relativeContext && (cell.Style.Width.Percent || cell.Style.Height.Percent);
                var intrinsic = Intrinsic(cell.Blocks);
                var inset = cell.Style.Padding + cell.Style.Border;
                var min = intrinsic.Min + inset.Left + inset.Right;
                var desired = Math.Max(min,
                    cell.Style.Width.Resolve(available, intrinsic.Preferred + inset.Left + inset.Right));
                Distribute(minimum, cell.Column, cell.ColumnSpan, min - gap * (cell.ColumnSpan - 1));
                Distribute(preferred, cell.Column, cell.ColumnSpan, desired - gap * (cell.ColumnSpan - 1));
            }

            var minimumTotal = minimum.Sum();
            var preferredTotal = preferred.Sum();
            box.PreferredContentWidth = Math.Min(width, preferredTotal + gap * (table.Columns + 1));
            var columns = new double[table.Columns];
            if (minimumTotal > available)
            {
                for (var column = 0; column < columns.Length; column++)
                    columns[column] = available * minimum[column] / minimumTotal;
            }
            else
            {
                var fraction = preferredTotal > minimumTotal
                    ? Math.Min(1, (available - minimumTotal) / (preferredTotal - minimumTotal))
                    : 0;
                for (var column = 0; column < columns.Length; column++)
                    columns[column] = minimum[column] + fraction * Math.Max(0, preferred[column] - minimum[column]);
                var extra = (available - columns.Sum()) / columns.Length;
                for (var column = 0; column < columns.Length; column++) columns[column] += extra;
            }

            var columnStarts = new double[table.Columns + 1];
            columnStarts[0] = x + gap;
            for (var column = 0; column < table.Columns; column++)
                columnStarts[column + 1] = columnStarts[column] + columns[column] + gap;
            var cells = new List<(PinnedRichCell Source, PinnedRichBox Box, double ContentHeight)>();
            var heights = new double[table.Rows];
            foreach (var cell in table.Cells.OrderBy(cell => cell.RowSpan))
            {
                _token.ThrowIfCancellationRequested();
                var cellWidth = columnStarts[cell.Column + cell.ColumnSpan] - columnStarts[cell.Column] - gap;
                var cellBox = new PinnedRichBox
                {
                    Bounds = new(columnStarts[cell.Column], 0, cellWidth, 0), Style = cell.Style,
                };
                var inset = cell.Style.Padding + cell.Style.Border;
                var contentHeight = LayoutBlocks(cell.Blocks, cellBox.Children,
                    cellBox.Bounds.X + inset.Left, inset.Top, Math.Max(1, cellWidth - inset.Left - inset.Right));
                var height = Math.Max(contentHeight + inset.Top + inset.Bottom, cell.Style.Height.Resolve(width, 0));
                cellBox.Bounds = cellBox.Bounds.WithHeight(height);
                Distribute(heights, cell.Row, cell.RowSpan, height - gap * (cell.RowSpan - 1));
                cells.Add((cell, cellBox, contentHeight));
            }

            var rows = new double[table.Rows + 1];
            rows[0] = y + gap;
            for (var row = 0; row < table.Rows; row++) rows[row + 1] = rows[row] + heights[row] + gap;
            foreach (var (cell, cellBox, contentHeight) in cells.OrderBy(item => item.Source.Row)
                         .ThenBy(item => item.Source.Column))
            {
                var height = rows[cell.Row + cell.RowSpan] - rows[cell.Row] - gap;
                cellBox.Move(new(0, rows[cell.Row]));
                cellBox.Bounds = cellBox.Bounds.WithHeight(height);
                var inset = cell.Style.Padding + cell.Style.Border;
                var extra = Math.Max(0, height - inset.Top - inset.Bottom - contentHeight);
                var shift = cell.VerticalAlignment switch { "bottom" => extra, "top" => 0, _ => extra / 2 };
                if (shift > 0)
                    foreach (var child in cellBox.Children)
                        child.Move(new(0, shift));
                box.Children.Add(cellBox);
            }

            return rows[^1] - y;
        }

        private static void Distribute(double[] values, int start, int count, double wanted)
        {
            var current = 0d;
            for (var index = start; index < start + count; index++) current += values[index];
            var extra = Math.Max(0, wanted - current) / count;
            for (var index = start; index < start + count; index++) values[index] += extra;
        }

        private (double Min, double Preferred) Intrinsic(IReadOnlyList<PinnedRichBlock> blocks)
        {
            var minimum = 1d;
            var preferred = 1d;
            foreach (var block in blocks)
            {
                _token.ThrowIfCancellationRequested();
                var content = block switch
                {
                    PinnedRichParagraph paragraph => IntrinsicParagraph(paragraph),
                    PinnedRichContainer container => Intrinsic(container.Blocks),
                    PinnedRichTable table => (Math.Min(400, table.Columns * 24d), Math.Min(6000, table.Columns * 120d)),
                    _ => (1d, 1d),
                };
                var style = block.Style;
                var inset = style.Padding + style.Border + style.Margin;
                minimum = Math.Max(minimum, content.Item1 + inset.Left + inset.Right);
                preferred = Math.Max(preferred, content.Item2 + inset.Left + inset.Right);
            }

            return (minimum, preferred);
        }

        private (double Min, double Preferred) IntrinsicParagraph(PinnedRichParagraph paragraph)
        {
            var preferred = 0d;
            var minimum = 1d;
            foreach (var run in paragraph.Runs)
            {
                if (run.Image is { } image)
                {
                    var width = image.Width.Resolve(600, _imageSize(image).Width);
                    minimum = Math.Max(minimum, Math.Min(600, width));
                    preferred += width;
                    continue;
                }

                var face = new Typeface(
                    run.Style.FontFamily is { Length: > 0 } family ? new(family) : FontFamily.Default,
                    run.Style.Slant, run.Style.Weight);
                foreach (var chunk in run.Text.Chunk(512))
                {
                    using var text = new TextLayout(new(chunk), face, run.Style.FontSize, _foreground);
                    preferred += text.WidthIncludingTrailingWhitespace;
                }

                foreach (var word in run.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                {
                    using var text = new TextLayout(word.Length > 128 ? word[..128] : word,
                        face, run.Style.FontSize, _foreground);
                    minimum = Math.Max(minimum, Math.Min(600, text.WidthIncludingTrailingWhitespace));
                }
            }

            return (minimum, Math.Max(minimum, Math.Min(6000, preferred)));
        }
    }
}

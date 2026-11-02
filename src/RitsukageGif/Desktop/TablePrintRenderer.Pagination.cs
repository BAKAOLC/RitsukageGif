namespace RitsukageGif.Desktop
{
    internal sealed partial class TablePrintRenderer
    {
        private static List<TablePrintPage> Paginate(TableRecognitionResult table, DocumentOutputOptions options,
            int[] rows, int panels, double height, double pitch, CancellationToken token)
        {
            if (options.PageMode == DocumentPageMode.Fit) return [new(0, 0, rows[^1])];
            var capacity = (int)Math.Floor(height / pitch);
            if (capacity < 1)
                throw new ArgumentException("Table print page cannot contain one text line.", nameof(options));
            var crossing = new int[rows.Length];
            foreach (var cell in table.Cells)
                if (cell.RowSpan > 1)
                {
                    crossing[cell.Row + 1]++;
                    crossing[cell.Row + cell.RowSpan]--;
                }

            var active = 0;
            var boundaries = new List<int>();
            var unmerged = new List<int>();
            for (var row = 1; row < rows.Length; row++)
            {
                active += crossing[row];
                boundaries.Add(rows[row]);
                if (active == 0) unmerged.Add(rows[row]);
            }

            var cuts = new List<(int Start, int End)>();
            var start = 0;
            while (start < rows[^1])
            {
                token.ThrowIfCancellationRequested();
                var end = Math.Min(rows[^1], start + capacity);
                if (end < rows[^1])
                {
                    var boundary = LastBoundary(boundaries, end);
                    var unmergedBoundary = LastBoundary(unmerged, end);
                    var nextGroupBoundary = NextBoundary(unmerged, unmergedBoundary);
                    if (unmergedBoundary > start && nextGroupBoundary - unmergedBoundary <= capacity)
                        end = unmergedBoundary;
                    else if (boundary > start && NextBoundary(boundaries, boundary) - boundary <= capacity)
                        end = boundary;
                }

                cuts.Add((start, end));
                if ((long)cuts.Count * panels > MaximumPages)
                    throw new InvalidOperationException("Table print exceeds the 1024-page budget.");
                start = end;
            }

            var pages = new List<TablePrintPage>();
            if (options.PaginationDirection == DocumentPaginationDirection.Vertical)
                for (var panel = 0; panel < panels; panel++)
                    // ReSharper disable once ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator
                    foreach (var cut in cuts)
                        pages.Add(new(panel, cut.Start, cut.End));
            else
                foreach (var cut in cuts)
                    for (var panel = 0; panel < panels; panel++)
                        pages.Add(new(panel, cut.Start, cut.End));
            return pages;
        }

        private static int LastBoundary(List<int> boundaries, int end)
        {
            var index = boundaries.BinarySearch(end);
            if (index < 0) index = ~index - 1;
            return index < 0 ? 0 : boundaries[index];
        }

        private static int NextBoundary(List<int> boundaries, int start)
        {
            var index = boundaries.BinarySearch(start);
            index = index < 0 ? ~index : index + 1;
            return index < boundaries.Count ? boundaries[index] : boundaries[^1];
        }
    }
}

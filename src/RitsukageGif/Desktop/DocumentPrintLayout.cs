namespace RitsukageGif.Desktop
{
    internal static class DocumentPrintLayout
    {
        internal static void Validate(double dpi, double widthPoints, double heightPoints)
        {
            if (!double.IsFinite(dpi) || dpi is < 1 or > 2400 || !double.IsFinite(widthPoints) ||
                widthPoints is <= 0 or > 14400 ||
                !double.IsFinite(heightPoints) || heightPoints is <= 0 or > 14400)
                throw new ArgumentOutOfRangeException(nameof(dpi), "The print page dimensions are not supported.");
        }

        internal static void ValidateRasterSize(int width, int height, double dpi, double widthPoints,
            double heightPoints)
        {
            if (Math.Abs(width - widthPoints / 72 * dpi) > 1.000001 ||
                Math.Abs(height - heightPoints / 72 * dpi) > 1.000001)
                throw new InvalidDataException("A print page's raster and physical dimensions do not match.");
        }

        internal static PrintPagePlacement Place(double widthPoints, double heightPoints, int printableWidth,
            int printableHeight,
            int printerDpiX, int printerDpiY)
        {
            if (printableWidth <= 0 || printableHeight <= 0 || printerDpiX <= 0 || printerDpiY <= 0)
                throw new InvalidOperationException("The printer has no printable area.");
            var requestedWidth = widthPoints / 72 * printerDpiX;
            var requestedHeight = heightPoints / 72 * printerDpiY;
            var fit = Math.Min(1, Math.Min(printableWidth / requestedWidth, printableHeight / requestedHeight));
            var width = Math.Max(1, (int)Math.Round(requestedWidth * fit));
            var height = Math.Max(1, (int)Math.Round(requestedHeight * fit));
            return new((printableWidth - width) / 2, (printableHeight - height) / 2, width, height);
        }

        internal static bool[] SelectPages(int pageCount, bool all, ReadOnlySpan<PrintPageRange> ranges)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageCount);
            var selected = new bool[pageCount];
            if (all)
            {
                Array.Fill(selected, true);
            }
            else
            {
                if (ranges.IsEmpty) throw new InvalidDataException("No print page range was selected.");
                foreach (var range in ranges)
                {
                    if (range.From < 1 || range.To > pageCount || range.To < range.From)
                        throw new InvalidDataException("The selected print page range is invalid.");
                    for (var page = range.From; page <= range.To; page++) selected[page - 1] = true;
                }
            }

            return selected;
        }

        internal static RasterPrintPage[] Snapshot(IReadOnlyList<RasterPrintPage> pages)
        {
            if (pages.Count is < 1 or > 4096) throw new ArgumentOutOfRangeException(nameof(pages));
            var snapshot = pages.ToArray();
            long bytes = 0;
            foreach (var page in snapshot)
            {
                Validate(page.Dpi, page.WidthPoints, page.HeightPoints);
                if (page.Png.IsEmpty) throw new ArgumentException("A print page has no image.", nameof(pages));
                bytes += page.Png.Length;
                if (bytes > 256L * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(pages));
            }

            return snapshot;
        }
    }
}

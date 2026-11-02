using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class TablePrintRenderer : ITablePrintRenderer
    {
        private const int MaximumPages = 1024;
        private const long MaximumPngBytes = 256 * 1024 * 1024;

        internal static DocumentOutputOptions DefaultOptions => new()
        {
            PaperSize = DocumentPaperSize.A4,
            Orientation = DocumentOrientation.Portrait,
            PageMode = DocumentPageMode.Paginate,
            PaginationDirection = DocumentPaginationDirection.Vertical,
            Dpi = 150,
            Margins = new() { Left = 12, Top = 12, Right = 12, Bottom = 12 },
        };

        public async Task<IReadOnlyList<RasterPrintPage>> RenderAsync(TableRecognitionResult table,
            DocumentOutputOptions options, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(table);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(table.Cells);
            cancellationToken.ThrowIfCancellationRequested();
            if (table.Cells.Count > 200_000)
                throw new ArgumentException("Table print exceeds the cell budget.", nameof(table));
            var snapshot = table with { Cells = [.. table.Cells] };
            using var document = await Task.Run(() => Measure(snapshot, options, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
            var pages = new List<RasterPrintPage>();
            long bytes = 0;
            var widthPoints = document.Width * 72 / 96;
            var heightPoints = document.Height * 72 / 96;
            foreach (var page in document.Pages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var bitmap = await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return Draw(document, page, options, cancellationToken);
                }, DispatcherPriority.Background, cancellationToken);
                // ReSharper disable once AccessToDisposedClosure
                var rendered = await Task
                    .Run(
                        () => Encode(bitmap, options.Dpi, widthPoints, heightPoints,
                            cancellationToken), cancellationToken)
                    .ConfigureAwait(false);
                bytes += rendered.Png.Length;
                if (bytes > MaximumPngBytes)
                    throw new InvalidOperationException("Table print output exceeds the encoded page budget.");
                pages.Add(rendered);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return pages;
        }
    }
}

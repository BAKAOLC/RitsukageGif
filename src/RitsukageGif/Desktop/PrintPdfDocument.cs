using SkiaSharp;

namespace RitsukageGif.Desktop
{
    internal static class PrintPdfDocument
    {
        internal static Task WriteAsync(string path, IReadOnlyList<RasterPrintPage> pages,
            CancellationToken token)
        {
            var snapshot = DocumentPrintLayout.Snapshot(pages);
            return Task.Run(() =>
            {
                using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var document = SKDocument.CreatePdf(stream, new SKDocumentPdfMetadata { EncodingQuality = 101 });
                foreach (var page in snapshot)
                {
                    token.ThrowIfCancellationRequested();
                    using var bitmap = SKBitmap.Decode(page.Png.Span) ??
                                       throw new InvalidDataException("A print page could not be decoded.");
                    DocumentPrintLayout.ValidateRasterSize(bitmap.Width, bitmap.Height, page.Dpi,
                        page.WidthPoints, page.HeightPoints);
                    var canvas = document.BeginPage((float)page.WidthPoints, (float)page.HeightPoints);
                    canvas.Clear(SKColors.White);
                    using var image = SKImage.FromBitmap(bitmap);
                    canvas.DrawImage(image, new SKRect(0, 0, (float)page.WidthPoints, (float)page.HeightPoints));
                    document.EndPage();
                }

                token.ThrowIfCancellationRequested();
                document.Close();
            }, token);
        }
    }
}

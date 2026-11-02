using System.Diagnostics;
using System.Text.Json;

namespace RitsukageGif.Desktop
{
    internal static class NativePrintProcess
    {
        internal static Task<bool> PrintImageAsync(byte[] image, string title, CancellationToken token,
            Func<ProcessStartInfo>? startInfoFactory = null)
        {
            return PrintAsync(title, async (directory, geometry, cancellation) =>
            {
                var output = Path.Combine(directory, "document.pdf");
                await new PdfDocumentService().SaveImageAsync(image, output,
                    geometry.Apply(new() { PageMode = DocumentPageMode.Fit }), cancellation).ConfigureAwait(false);
                return output;
            }, startInfoFactory, token);
        }

        internal static Task<bool> PrintTableAsync(TableRecognitionResult table, DocumentOutputOptions options,
            string title, ITablePrintRenderer renderer, CancellationToken token,
            Func<ProcessStartInfo>? startInfoFactory = null)
        {
            var snapshot = table with { Cells = [.. table.Cells] };
            return PrintAsync(title, async (directory, geometry, cancellation) =>
            {
                var pages = await renderer.RenderAsync(snapshot, geometry.Apply(options), cancellation)
                    .ConfigureAwait(false);
                foreach (var page in pages)
                    if (Math.Abs(page.WidthPoints - geometry.PaperWidthPoints) > .1 ||
                        Math.Abs(page.HeightPoints - geometry.PaperHeightPoints) > .1)
                        throw new InvalidDataException("A rendered page does not match the selected printer paper.");
                var output = Path.Combine(directory, "document.pdf");
                await PrintPdfDocument.WriteAsync(output, pages, cancellation).ConfigureAwait(false);
                return output;
            }, startInfoFactory, token);
        }

        private static Task<bool> PrintAsync(string title,
            Func<string, PrintPageGeometry, CancellationToken, Task<string>> prepare,
            Func<ProcessStartInfo>? startInfoFactory, CancellationToken token)
        {
            return PrintWorkerTransport.RunNativeAsync("", title, "--native-print-worker",
                (directory, _) => Task.FromResult(directory),
                startInfoFactory ?? (() => new(Path.Combine(AppContext.BaseDirectory, "RitsukageGif.Print"))),
                (directory, json, cancellation) =>
                {
                    var geometry = JsonSerializer.Deserialize(json, NativePrintJsonContext.Default.PrintPageGeometry)
                                   ?? throw new InvalidDataException("The native printer returned no page geometry.");
                    geometry.Validate();
                    return prepare(directory, geometry, cancellation);
                }, token);
        }
    }
}

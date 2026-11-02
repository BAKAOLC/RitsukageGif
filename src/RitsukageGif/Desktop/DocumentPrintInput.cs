using System.Text.Json;

namespace RitsukageGif.Desktop
{
    internal static class DocumentPrintInput
    {
        internal static async Task<string> WriteAsync(string directory, IReadOnlyList<RasterPrintPage> pages,
            CancellationToken token)
        {
            var files = new PrintPageFile[pages.Count];
            for (var index = 0; index < pages.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                var page = pages[index];
                var path = Path.Combine(directory, $"page-{index:D4}.png");
                await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                                 65536,
                                 FileOptions.Asynchronous | FileOptions.SequentialScan))
                    await stream.WriteAsync(page.Png, token).ConfigureAwait(false);
                files[index] = new(path, page.Dpi, page.WidthPoints, page.HeightPoints);
            }

            var manifest = Path.Combine(directory, "pages.json");
            await File.WriteAllTextAsync(manifest,
                    JsonSerializer.Serialize(files, DocumentPrintJsonContext.Default.PrintPageFileArray), token)
                .ConfigureAwait(false);
            return manifest;
        }

        internal static PrintPageFile[] Read(string manifest)
        {
            var pages = JsonSerializer.Deserialize(File.ReadAllText(manifest),
                            DocumentPrintJsonContext.Default.PrintPageFileArray)
                        ?? throw new InvalidDataException("The print page manifest is empty.");
            if (pages.Length is < 1 or > 4096) throw new InvalidDataException("The print page count is invalid.");
            foreach (var page in pages) DocumentPrintLayout.Validate(page.Dpi, page.WidthPoints, page.HeightPoints);
            return pages;
        }
    }
}

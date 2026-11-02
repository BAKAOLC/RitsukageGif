using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace RitsukageGif.Desktop
{
    internal static class PinnedRichRender
    {
        internal static async Task<byte[]> EncodeAsync(PinnedRichDocument document, PinnedRichResourceLoader resources,
            double width, double scale, IBrush background, IBrush foreground, CancellationToken token)
        {
            var natural = new Dictionary<string, Size>(StringComparer.Ordinal);
            foreach (var image in Images(document.Blocks).DistinctBy(image => image.Source))
            {
                using var resource = await resources.AcquireAsync(image.Source, document.BaseUri, token);
                natural[image.Source] = resource.Bitmap?.Size ?? new(32, 32);
            }

            var leases = new Dictionary<string, PinnedRichResource>(StringComparer.Ordinal);
            using var layout = new PinnedRichLayout(document, width, foreground,
                image => natural.GetValueOrDefault(image.Source, new(32, 32)),
                (context, image, bounds) =>
                {
                    if (leases.TryGetValue(image.Source, out var resource) && resource.Bitmap is { } bitmap)
                        context.DrawImage(bitmap, new(bitmap.Size), bounds);
                    else context.DrawRectangle(null, new Pen(Brushes.Gray), bounds);
                }, token);
            var pixelWidth = checked((int)Math.Ceiling(layout.Size.Width * scale));
            var pixelHeight = checked((int)Math.Ceiling(layout.Size.Height * scale));
            if ((long)pixelWidth * pixelHeight > 32L * 1024 * 1024)
                throw new InvalidOperationException("HTML image output exceeds the 32 megapixel budget.");
            using var output = new RenderTargetBitmap(new(pixelWidth, pixelHeight), new(96 * scale, 96 * scale));
            using (var context = output.CreateDrawingContext())
            {
                context.FillRectangle(background, new(default, layout.Size));
                for (var top = 0d; top < layout.Size.Height; top += 720)
                {
                    token.ThrowIfCancellationRequested();
                    var strip = new Rect(0, top, layout.Size.Width, Math.Min(720, layout.Size.Height - top));
                    try
                    {
                        foreach (var source in layout.ImageSources(strip).Distinct(StringComparer.Ordinal))
                            leases.Add(source, await resources.AcquireAsync(source, document.BaseUri, token));
                        using var clip = context.PushClip(strip);
                        layout.Draw(context, strip);
                    }
                    finally
                    {
                        foreach (var resource in leases.Values) resource.Dispose();
                        leases.Clear();
                    }
                }
            }

            token.ThrowIfCancellationRequested();
            using var encoded = new MemoryStream();
            output.Save(encoded, PngBitmapEncoderOptions.Default);
            return encoded.ToArray();
        }

        private static IEnumerable<PinnedRichImage> Images(IReadOnlyList<PinnedRichBlock> blocks)
        {
            foreach (var block in blocks)
                switch (block)
                {
                    case PinnedRichParagraph paragraph:
                        foreach (var run in paragraph.Runs)
                            if (run.Image is { } image)
                                yield return image;
                        break;
                    case PinnedRichContainer container:
                        foreach (var image in Images(container.Blocks)) yield return image;
                        break;
                    case PinnedRichTable table:
                        foreach (var cell in table.Cells)
                        foreach (var image in Images(cell.Blocks))
                            yield return image;
                        break;
                }
        }
    }
}

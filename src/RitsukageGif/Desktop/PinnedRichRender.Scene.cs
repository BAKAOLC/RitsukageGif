using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace RitsukageGif.Desktop
{
    internal static partial class PinnedRichRender
    {
        internal static async Task<RenderTargetBitmap> RenderAsync(PinnedRichDocument document,
            PinnedRichResourceLoader resources, double width, double scale, IBrush background, IBrush foreground,
            CancellationToken token, IReadOnlyDictionary<string, Size>? imageSizes = null)
        {
            var natural = new Dictionary<string, Size>(StringComparer.Ordinal);
            foreach (var image in Images(document.Blocks).DistinctBy(image => image.Source))
            {
                using var resource = await resources.AcquireAsync(image.Source, document.BaseUri, token);
                natural[image.Source] = imageSizes?.GetValueOrDefault(image.Source, new(32, 32)) ??
                                        resource.Bitmap?.Size ?? new(32, 32);
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
            var output = new RenderTargetBitmap(new(pixelWidth, pixelHeight), new(96 * scale, 96 * scale));
            try
            {
                using var context = output.CreateDrawingContext();
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

                return output;
            }
            catch
            {
                output.Dispose();
                throw;
            }
        }
    }
}

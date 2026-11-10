using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform;

namespace RitsukageGif.Desktop
{
    internal static class ImageTranslationRenderer
    {
        internal static Task<ImageTranslationRenderResult> RenderAsync(ImageTranslationDocument document,
            ImageTranslationBackground background,
            CancellationToken token, bool includeTextOverlay = false)
        {
            using var image = new WriteableBitmap(new(background.Width, background.Height), new(96, 96),
                PixelFormat.Bgra8888, AlphaFormat.Unpremul);
            using (var frame = image.Lock())
                for (var row = 0; row < background.Height; row++)
                    Marshal.Copy(background.Pixels, row * background.Width * 4,
                        frame.Address + row * frame.RowBytes, background.Width * 4);
            var layouts = new List<(TextLayout Layout, Rect Bounds)>();
            var text = new ImageTextSelectionBuilder();
            var line = 0;
            try
            {
                foreach (var block in document.Blocks)
                {
                    token.ThrowIfCancellationRequested();
                    var bounds = new Rect(block.Bounds.X, block.Bounds.Y, block.Bounds.Width, block.Bounds.Height);
                    var brush = new SolidColorBrush(Color.FromUInt32(background.Foreground[block.Id]));
                    var layout = Fit(block.TranslatedText, bounds.Size, brush,
                        ImageTextSelectionBuilder.Direction(block.TranslatedText));
                    layouts.Add((layout, bounds));
                    text.AppendLayout(block.TranslatedText, layout,
                        new(bounds.X, bounds.Y + (bounds.Height - layout.Height) / 2), bounds, line++);
                }

                var surface = new ImageTranslationCanvas(image, layouts);
                surface.Measure(new(background.Width, background.Height));
                surface.Arrange(new(0, 0, background.Width, background.Height));
                using var target = new RenderTargetBitmap(new(background.Width, background.Height), new(96, 96));
                target.Render(surface);
                token.ThrowIfCancellationRequested();
                using (var frame = image.Lock())
                {
                    target.CopyPixels(frame);
                    for (var row = 0; row < background.Height; row++)
                        Marshal.Copy(frame.Address + row * frame.RowBytes, background.Pixels,
                            row * background.Width * 4, background.Width * 4);
                }

                byte[]? textOverlay = null;
                if (includeTextOverlay)
                {
                    var glyphSurface = new ImageTranslationCanvas(null, layouts);
                    TextOptions.SetTextRenderingMode(glyphSurface, TextRenderingMode.Antialias);
                    glyphSurface.Measure(new(background.Width, background.Height));
                    glyphSurface.Arrange(new(0, 0, background.Width, background.Height));
                    using var glyphTarget =
                        new RenderTargetBitmap(new(background.Width, background.Height), new(96, 96));
                    glyphTarget.Render(glyphSurface);
                    using var glyphImage = new WriteableBitmap(new(background.Width, background.Height), new(96, 96),
                        PixelFormat.Bgra8888, AlphaFormat.Unpremul);
                    using var frame = glyphImage.Lock();
                    glyphTarget.CopyPixels(frame);
                    textOverlay = new byte[checked(background.Width * background.Height * 4)];
                    for (var row = 0; row < background.Height; row++)
                        Marshal.Copy(frame.Address + row * frame.RowBytes, textOverlay,
                            row * background.Width * 4, background.Width * 4);
                }

                var selectedText = text.Build();
                return Task.Run(() =>
                {
                    for (var offset = 0; offset < background.Pixels.Length; offset += 4)
                    {
                        if ((offset & 16383) == 0) token.ThrowIfCancellationRequested();
                        (background.Pixels[offset], background.Pixels[offset + 2]) =
                            (background.Pixels[offset + 2], background.Pixels[offset]);
                    }

                    var png = PngImageCodec.Encode(new(background.Width, background.Height, 8, 6,
                        background.Pixels, [("sRGB", new byte[] { 0 })]), token);
                    return new ImageTranslationRenderResult(png, selectedText) { TextOverlay = textOverlay };
                }, token);
            }
            finally
            {
                foreach (var (layout, _) in layouts) layout.Dispose();
            }
        }

        internal static ImageTextSelectionDocument BuildSelectionDocument(ImageTranslationDocument document,
            CancellationToken token)
        {
            var text = new ImageTextSelectionBuilder();
            var line = 0;
            foreach (var block in document.Blocks)
            {
                token.ThrowIfCancellationRequested();
                var bounds = new Rect(block.Bounds.X, block.Bounds.Y, block.Bounds.Width, block.Bounds.Height);
                using var layout = Fit(block.TranslatedText, bounds.Size, Brushes.Black,
                    ImageTextSelectionBuilder.Direction(block.TranslatedText));
                text.AppendLayout(block.TranslatedText, layout,
                    new(bounds.X, bounds.Y + (bounds.Height - layout.Height) / 2), bounds, line++);
            }

            return text.Build();
        }

        private static TextLayout Fit(string text, Size bounds, IBrush color, FlowDirection direction)
        {
            var lower = .5;
            var upper = Math.Max(lower, bounds.Height);
            TextLayout? best = null;
            for (var iteration = 0; iteration < 12; iteration++)
            {
                var size = (lower + upper) / 2;
                var candidate = new TextLayout(text, Typeface.Default, size, color,
                    direction == FlowDirection.RightToLeft ? TextAlignment.Right : TextAlignment.Left,
                    TextWrapping.Wrap, flowDirection: direction, maxWidth: bounds.Width);
                if (candidate.Height <= bounds.Height && candidate.WidthIncludingTrailingWhitespace <= bounds.Width)
                {
                    best?.Dispose();
                    best = candidate;
                    lower = size;
                }
                else
                {
                    candidate.Dispose();
                    upper = size;
                }
            }

            return best ?? throw new InvalidDataException("The translated text cannot fit in its source image region.");
        }
    }
}

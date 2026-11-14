using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.Media.TextFormatting;
using Avalonia_PixelPoint = Avalonia.PixelPoint;

namespace RitsukageGif.Desktop
{
    internal static class PinnedTextDragPreview
    {
        private const int MaximumWidth = 500;
        private const int MaximumHeight = 300;
        private const int FontSize = 16;
        private const int Padding = 10;
        private const int CornerRadius = 8;
        private const string OverflowMarker = "...";
        private const string Ellipsis = "…";
        private static readonly IBrush Background = new ImmutableSolidColorBrush(Color.FromArgb(180, 255, 255, 255));
        private static readonly IBrush Foreground = new ImmutableSolidColorBrush(Color.FromArgb(180, 0, 0, 0));

        internal static (RenderTargetBitmap Bitmap, Avalonia_PixelPoint HotSpot)? Create(string text, Typeface typeface,
            double scale)
        {
            if (text.Length == 0) return null;
            var padding = (int)(Padding * scale);
            var fontSize = Math.Max(1, (int)(FontSize * scale));
            var maxWidth = (int)(MaximumWidth * scale);
            var maxHeight = (int)(MaximumHeight * scale);
            var contentWidth = maxWidth - 2 * padding;
            var contentHeight = maxHeight - 2 * padding;
            using var metrics = CreateLine("Mg", typeface, fontSize);
            var lineHeight = Math.Ceiling(metrics.Height);
            var maxLines = (int)(contentHeight / lineHeight);
            if (maxLines <= 0 || contentWidth <= 0) return null;
            var lines = new List<TextLayout>(maxLines);
            try
            {
                var remaining = text.AsSpan();
                var width = 0d;
                for (var index = 0; index < maxLines; index++)
                {
                    var separator = remaining.IndexOf('\n');
                    var line = (separator < 0 ? remaining : remaining[..separator]).ToString();
                    var layout = FitLine(line, typeface, fontSize, contentWidth, out var fitted);
                    if (index == maxLines - 1 && separator >= 0 &&
                        !fitted.EndsWith(OverflowMarker, StringComparison.Ordinal))
                    {
                        layout.Dispose();
                        layout = FitLine(fitted + OverflowMarker, typeface, fontSize, contentWidth, out _);
                    }

                    lines.Add(layout);
                    width = Math.Max(width, layout.WidthIncludingTrailingWhitespace);
                    if (separator < 0) break;
                    remaining = remaining[(separator + 1)..];
                }

                var size = new PixelSize(Math.Min(maxWidth, (int)Math.Ceiling(width) + 2 * padding),
                    Math.Min(maxHeight, (int)(lines.Count * lineHeight) + 2 * padding));
                var target = new RenderTargetBitmap(size, new(96, 96));
                try
                {
                    using var context = target.CreateDrawingContext();
                    var radius = CornerRadius * scale;
                    var bounds = new Rect(0, 0, size.Width, size.Height);
                    context.DrawRectangle(Background, null, bounds, radius, radius);
                    using var clip = context.PushClip(bounds.Deflate(new Thickness(padding)));
                    for (var i = 0; i < lines.Count; i++)
                        lines[i].Draw(context, new(padding, padding + i * lineHeight));
                    return (target, new(size.Width / 2, size.Height / 2));
                }
                catch
                {
                    target.Dispose();
                    throw;
                }
            }
            finally
            {
                foreach (var line in lines) line.Dispose();
            }
        }

        private static TextLayout FitLine(string text, Typeface typeface, int size, int width, out string fitted)
        {
            fitted = text;
            var full = CreateLine(text, typeface, size);
            if (full.WidthIncludingTrailingWhitespace <= width) return full;
            full.Dispose();
            var boundaries = StringInfo.ParseCombiningCharacters(text);
            var low = 0;
            var high = boundaries.Length - 1;
            while (low < high)
            {
                var count = (low + high + 1) / 2;
                using var trial = CreateLine(Elide(count), typeface, size);
                if (trial.WidthIncludingTrailingWhitespace <= width) low = count;
                else high = count - 1;
            }

            fitted = Elide(low);
            return CreateLine(fitted, typeface, size);

            string Elide(int count)
            {
                var prefix = (count + 1) / 2;
                var suffix = count - prefix;
                return string.Concat(text.AsSpan(0, boundaries[prefix]), Ellipsis,
                    text.AsSpan(suffix == 0 ? text.Length : boundaries[^suffix]));
            }
        }

        private static TextLayout CreateLine(string text, Typeface typeface, int size)
        {
            return new(text, typeface, size, Foreground,
                flowDirection: ImageTextSelectionBuilder.Direction(text), textWrapping: TextWrapping.NoWrap);
        }
    }
}

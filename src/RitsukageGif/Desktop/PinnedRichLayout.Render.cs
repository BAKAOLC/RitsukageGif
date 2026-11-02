using Avalonia;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedRichLayout
    {
        internal void Draw(DrawingContext context, Rect viewport, int selectionStart = 0, int selectionLength = 0)
        {
            DrawBox(Root, context, viewport, selectionStart, selectionLength);
        }

        private void DrawBox(PinnedRichBox box, DrawingContext context, Rect viewport, int start, int length)
        {
            if (!box.VisualBounds.Intersects(viewport)) return;
            var style = box.Style;
            if (style.Background is { } background) context.FillRectangle(new SolidColorBrush(background), box.Bounds);
            if (box.Rule)
                context.DrawLine(new Pen(new SolidColorBrush(style.BorderColor)), box.Bounds.TopLeft,
                    box.Bounds.TopRight);
            var edges = style.Border;
            var brush = new SolidColorBrush(style.BorderColor);
            var bounds = box.Bounds;
            if (edges.Top > 0) context.FillRectangle(brush, new(bounds.X, bounds.Y, bounds.Width, edges.Top));
            if (edges.Bottom > 0)
                context.FillRectangle(brush, new(bounds.X, bounds.Bottom - edges.Bottom, bounds.Width, edges.Bottom));
            if (edges.Left > 0) context.FillRectangle(brush, new(bounds.X, bounds.Y, edges.Left, bounds.Height));
            if (edges.Right > 0)
                context.FillRectangle(brush, new(bounds.Right - edges.Right, bounds.Y, edges.Right, bounds.Height));
            var first = 0;
            var last = box.Lines.Count;
            while (first < last)
            {
                var middle = first + (last - first) / 2;
                if (box.Lines[middle].Bounds.Bottom < viewport.Top) first = middle + 1;
                else last = middle;
            }

            for (var index = first; index < box.Lines.Count; index++)
            {
                var line = box.Lines[index];
                if (line.Bounds.Top > viewport.Bottom) break;
                if (!line.Bounds.Intersects(viewport)) continue;
                var formatted = Realize(line);
                formatted.Draw(context, line.Bounds.TopLeft);
                var left = Math.Max(start, line.Start);
                var right = Math.Min(start + length, line.Start + line.Length);
                if (right <= left) continue;
                foreach (var selected in formatted.GetTextBounds(left - line.Paragraph.Start, right - left))
                    context.FillRectangle(new SolidColorBrush(Color.FromArgb(95, 55, 140, 240)),
                        selected.Rectangle.Translate(line.Bounds.Position));
            }

            foreach (var child in box.Children) DrawBox(child, context, viewport, start, length);
        }

        internal int HitTest(Point point)
        {
            var nearest = _lines.MinBy(line => Distance(line.Bounds, point));
            if (nearest is null) return 0;
            var hit = Realize(nearest).GetCharacterHitFromDistance(point.X - nearest.Bounds.X);
            return Math.Clamp(nearest.Paragraph.Start + hit.FirstCharacterIndex + hit.TrailingLength,
                nearest.Start, nearest.Start + nearest.Length);
        }

        internal Rect CaretBounds(int position)
        {
            var line = _lines.FirstOrDefault(line => position >= line.Start && position <= line.Start + line.Length);
            if (line is null) return default;
            var distance = Realize(line).GetDistanceFromCharacterHit(new(position - line.Paragraph.Start));
            return new(line.Bounds.X + distance, line.Bounds.Y, 1, line.Bounds.Height);
        }

        internal string? LinkAt(Point point)
        {
            var position = HitTest(point);
            var line = _lines.FirstOrDefault(line => line.Bounds.Contains(point));
            return line?.Paragraph.Runs
                .FirstOrDefault(run => run.Start <= position && position < run.Start + run.Text.Length)?.Style.Link;
        }

        private static double Distance(Rect bounds, Point point)
        {
            var x = Math.Max(bounds.Left - point.X, Math.Max(0, point.X - bounds.Right));
            var y = Math.Max(bounds.Top - point.Y, Math.Max(0, point.Y - bounds.Bottom));
            return y * y + x * x;
        }
    }
}

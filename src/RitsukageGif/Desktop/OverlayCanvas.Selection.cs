using Avalonia;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class OverlayCanvas
    {
        private static readonly IPen HandleBorder = new Pen(Brushes.White, 1.5);

        private void DrawWindowFrame(DrawingContext context, PixelRect region)
        {
            if (WindowFrame is not { } frame) return;
            var intersection = region.Intersect(frame.Region);
            if (intersection.IsEmpty) return;
            var local = intersection.ToLocal(_monitor, Scale);
            context.DrawImage(frame.Bitmap,
                new(intersection.X - frame.Region.X, intersection.Y - frame.Region.Y, intersection.Width,
                    intersection.Height),
                new(local.X, local.Y, local.Width, local.Height));
        }

        private void DrawSelectionHandles(DrawingContext context)
        {
            if (Selected.IsEmpty || _appearance?.SelectionAnchors == SelectionAnchorMode.None) return;
            var local = Selected.ToLocal(_monitor, Scale);
            var area = new Rect(local.X, local.Y, local.Width, local.Height);
            Span<Point> points =
            [
                area.TopLeft, new(area.Center.X, area.Top), area.TopRight, new(area.Right, area.Center.Y),
                area.BottomRight, new(area.Center.X, area.Bottom), area.BottomLeft, new(area.Left, area.Center.Y),
            ];
            using var clip = context.PushClip(new(Bounds.Size));
            for (var index = 0; index < points.Length; index++)
            {
                if (_appearance?.SelectionAnchors == SelectionAnchorMode.Four && index % 2 != 0) continue;
                context.DrawEllipse(_selectionBrush, HandleBorder, points[index], 4, 4);
            }
        }
    }
}

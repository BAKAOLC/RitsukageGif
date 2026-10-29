using Avalonia;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class OverlayCanvas
    {
        private void DrawHdrDim(DrawingContext context, Rect all)
        {
            var selected = Selected.Intersect(_monitor);
            if (selected.IsEmpty)
            {
                context.FillRectangle(_dimBrush, all);
                return;
            }

            if (_frameDecoration.CornerRadius > 0 && !IsCandidate)
            {
                PrepareFrameDecoration();
                context.DrawGeometry(_dimBrush, null, _frameOutside!);
                return;
            }

            var local = selected.ToLocal(_monitor, Scale);
            var left = Math.Clamp(local.X, 0, all.Width);
            var top = Math.Clamp(local.Y, 0, all.Height);
            var right = Math.Clamp(local.X + local.Width, left, all.Width);
            var bottom = Math.Clamp(local.Y + local.Height, top, all.Height);
            context.FillRectangle(_dimBrush, new(0, 0, all.Width, top));
            context.FillRectangle(_dimBrush, new(0, bottom, all.Width, all.Height - bottom));
            context.FillRectangle(_dimBrush, new(0, top, left, bottom - top));
            context.FillRectangle(_dimBrush, new(right, top, all.Width - right, bottom - top));
        }
    }
}

using Avalonia;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class OverlayCanvas
    {
        internal CapturePreferences CapturePreferences { get; set; } = new();
        internal bool AuxiliaryVisible { get; set; }

        private void DrawAuxiliaryLines(DrawingContext context, Point mouse)
        {
            if (!AuxiliaryVisible) return;
            using var clip = context.PushClip(new(Bounds.Size));
            if (CapturePreferences.GuideCursor) DrawGuideCross(context, mouse);
            if (Selected.IsEmpty) return;
            var local = Selected.ToLocal(_monitor, Scale);
            var region = new Rect(local.X, local.Y, local.Width, local.Height);
            if (CapturePreferences.GuideSelectionCenter) DrawGuideCross(context, region.Center);
            if (CapturePreferences.GuideSelectionBorder)
            {
                DrawGuideCross(context, region.TopLeft);
                DrawGuideCross(context, region.BottomRight);
            }

            if (!CapturePreferences.GuideThirds) return;
            for (var step = 1; step <= 2; step++)
            {
                var x = region.Left + region.Width * step / 3;
                var y = region.Top + region.Height * step / 3;
                context.DrawLine(CursorPen, new(x, region.Top), new(x, region.Bottom));
                context.DrawLine(CursorPen, new(region.Left, y), new(region.Right, y));
            }
        }

        private void DrawGuideCross(DrawingContext context, Point point)
        {
            context.DrawLine(CursorPen, new(point.X, 0), new(point.X, Bounds.Height));
            context.DrawLine(CursorPen, new(0, point.Y), new(Bounds.Width, point.Y));
        }
    }
}

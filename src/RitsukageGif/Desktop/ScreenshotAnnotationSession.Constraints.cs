using Avalonia;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        public void MoveObjectWithModifiers(PixelPoint point, bool shift)
        {
            _shiftPressed = shift;
            MoveObject(point);
        }

        private Point ConstrainSegmentPoint(Point anchor, PixelPoint point, bool shift)
        {
            var current = new Point(Math.Clamp(point.X, AnnotationBounds.X, AnnotationBounds.Right),
                Math.Clamp(point.Y, AnnotationBounds.Y, AnnotationBounds.Bottom));
            if (!shift) return current;
            var delta = new Vector(current.X - anchor.X, current.Y - anchor.Y);
            var angle = Math.Round(Math.Atan2(delta.Y, delta.X) / (Math.PI / 4)) * Math.PI / 4;
            delta = new Vector(Math.Cos(angle), Math.Sin(angle)) * delta.Length;
            var fraction = 1d;
            fraction = delta.X switch
            {
                > 0 => Math.Min(fraction, (AnnotationBounds.Right - anchor.X) / delta.X),
                < 0 => Math.Min(fraction, (AnnotationBounds.X - anchor.X) / delta.X),
                _ => fraction,
            };
            fraction = delta.Y switch
            {
                > 0 => Math.Min(fraction, (AnnotationBounds.Bottom - anchor.Y) / delta.Y),
                < 0 => Math.Min(fraction, (AnnotationBounds.Y - anchor.Y) / delta.Y),
                _ => fraction,
            };
            return anchor + delta * fraction;
        }
    }
}

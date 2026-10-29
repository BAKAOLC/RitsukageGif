using Avalonia.Controls;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed class ScreenshotScrollingMaskSurface : Control
    {
        private readonly IBrush _border;
        private readonly double _borderWidth;
        private readonly IBrush _shade;
        private List<PixelRect> _borders = [];
        private PixelRect _capture;
        private bool _excluded;
        private PixelRect _monitor;
        private double _scale;

        public ScreenshotScrollingMaskSurface(AppearancePreferences appearance)
        {
            _shade = new SolidColorBrush(Color.FromUInt32(appearance.SelectionMaskColor));
            _border = new SolidColorBrush(Color.FromUInt32(appearance.SelectionBorderColor ?? appearance.AccentColor));
            _borderWidth = appearance.SelectionBorderWidth;
            IsHitTestVisible = false;
        }

        public void Update(PixelRect monitor, PixelRect capture, double scale, bool excluded)
        {
            _monitor = monitor;
            _capture = capture;
            _scale = scale;
            _excluded = excluded;
            _borders = ScreenshotScrollingWindow.CreateBorderRectangles(capture, [monitor],
                (int)Math.Ceiling(_borderWidth * scale));
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            var hole = _monitor.Intersect(_capture);
            if (hole.IsEmpty)
            {
                Fill(_monitor, _shade);
            }
            else
            {
                Fill(new(_monitor.X, _monitor.Y, _monitor.Width, hole.Y - _monitor.Y), _shade);
                Fill(new(_monitor.X, hole.Bottom, _monitor.Width, _monitor.Bottom - hole.Bottom), _shade);
                Fill(new(_monitor.X, hole.Y, hole.X - _monitor.X, hole.Height), _shade);
                Fill(new(hole.Right, hole.Y, _monitor.Right - hole.Right, hole.Height), _shade);
                foreach (var border in _borders)
                    if (_excluded || border.Intersect(_capture).IsEmpty)
                        Fill(border, _border);
            }

            return;

            void Fill(PixelRect rectangle, IBrush brush)
            {
                if (rectangle.IsEmpty) return;
                context.FillRectangle(brush, new((rectangle.X - _monitor.X) / _scale,
                    (rectangle.Y - _monitor.Y) / _scale, rectangle.Width / _scale, rectangle.Height / _scale));
            }
        }
    }
}

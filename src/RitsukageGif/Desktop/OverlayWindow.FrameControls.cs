using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class OverlayWindow
    {
        private double _frameClipRadius = -1;
        private PixelRect _frameClipRegion;
        private double _frameClipScale;

        private void UpdateFrameClip(PixelRect selected, CaptureDecoration decoration)
        {
            if (_annotationSurface is null) return;
            var radius = selected.IsEmpty
                ? 0
                : Math.Min(decoration.CornerRadius,
                    Math.Min(selected.Width, selected.Height) / 2.0);
            // ReSharper disable CompareOfFloatsByEqualityOperator
            if (_frameClipRegion == selected && _frameClipRadius == radius && _frameClipScale == _canvas.Scale) return;
            // ReSharper restore CompareOfFloatsByEqualityOperator
            _frameClipRegion = selected;
            _frameClipRadius = radius;
            _frameClipScale = _canvas.Scale;
            if (radius <= 0)
            {
                _annotationSurface.Clip = null;
                return;
            }

            var local = selected.ToLocal(Region, _canvas.Scale);
            _annotationSurface.Clip = new RectangleGeometry
            {
                Rect = new(local.X, local.Y, local.Width, local.Height),
                RadiusX = radius / _canvas.Scale, RadiusY = radius / _canvas.Scale,
            };
        }
    }
}

using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class OverlayCanvas
    {
        private SelectionShape? _cachedShape;
        private Geometry? _shapeGeometry;
        private double _shapeRadius;
        private PixelRect _shapeRegion;
        private double _shapeScale;

        internal Geometry? SelectionGeometry()
        {
            var shape = Snapshot?.Shape;
            if (shape is null) return null;
            var radius = IsCandidate ? 0 : FrameRadius;
            if (shape == _cachedShape && _shapeScale.Equals(Scale) && _shapeRadius.Equals(radius) &&
                _shapeRegion == Selected)
                return _shapeGeometry;
            _cachedShape = shape;
            _shapeScale = Scale;
            _shapeRadius = radius;
            _shapeRegion = Selected;
            var geometry = Geometry.Parse((shape.EvenOdd ? "F0 " : "F1 ") + shape.PathData);
            geometry.Transform = new MatrixTransform(new(1 / Scale, 0, 0, 1 / Scale,
                -_monitor.X / Scale, -_monitor.Y / Scale));
            if (radius > 0)
            {
                PrepareFrameDecoration();
                return _shapeGeometry = new CombinedGeometry(GeometryCombineMode.Intersect, geometry, _frameClip!);
            }

            return _shapeGeometry = geometry;
        }
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed class PinnedImageTransparencyBackground : Control, IPinnedInputCoverageSource
    {
        private static readonly IBrush Accepting = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        private PinnedTransparencySurface _surface;
        private IBrush? _brush;

        public event Action? InputCoverageChanged;

        internal void Update(PinnedTransparencySurface surface)
        {
            if (_surface == surface) return;
            _surface = surface;
            _brush = surface.Mode switch
            {
                PinnedTransparentLayerMode.IgnoreMouse => null,
                PinnedTransparentLayerMode.AcceptingMouse => Accepting,
                PinnedTransparentLayerMode.Checkerboard => CreateChecker(surface),
                _ => throw new ArgumentOutOfRangeException(nameof(surface)),
            };
            InvalidateVisual();
            InputCoverageChanged?.Invoke();
        }

        public override void Render(DrawingContext context)
        {
            if (_brush is not null) context.DrawRectangle(_brush, null, new(Bounds.Size));
        }

        private static DrawingBrush CreateChecker(PinnedTransparencySurface surface)
        {
            var cell = surface.CellSize;
            var tile = cell * 2;
            var diagonal = new SolidColorBrush(surface.Dark ? Color.FromRgb(50, 50, 50) : Color.FromRgb(192, 192, 192));
            var background = new SolidColorBrush(surface.Dark ? Color.FromRgb(71, 71, 71) : Colors.White);
            return new()
            {
                Drawing = new DrawingGroup
                {
                    Children =
                    {
                        new GeometryDrawing
                            { Brush = background, Geometry = new RectangleGeometry(new(0, 0, tile, tile)) },
                        new GeometryDrawing
                            { Brush = diagonal, Geometry = new RectangleGeometry(new(0, 0, cell, cell)) },
                        new GeometryDrawing
                            { Brush = diagonal, Geometry = new RectangleGeometry(new(cell, cell, cell, cell)) },
                    },
                },
                DestinationRect = new(-surface.SourceOffset.X % tile, -surface.SourceOffset.Y % tile,
                    tile, tile, RelativeUnit.Absolute),
                TileMode = TileMode.Tile,
            };
        }
    }
}

using Avalonia;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal static class ImageTransparencyBackground
    {
        private static readonly Color Light = Color.Parse("#F4F4F4");
        private static readonly Color Dark = Color.Parse("#DEDEDE");

        internal static IBrush Checker { get; } = new DrawingBrush
        {
            Drawing = new DrawingGroup
            {
                Children =
                {
                    new GeometryDrawing
                        { Brush = new SolidColorBrush(Light), Geometry = new RectangleGeometry(new(0, 0, 16, 16)) },
                    new GeometryDrawing
                        { Brush = new SolidColorBrush(Dark), Geometry = new RectangleGeometry(new(8, 0, 8, 8)) },
                    new GeometryDrawing
                        { Brush = new SolidColorBrush(Dark), Geometry = new RectangleGeometry(new(0, 8, 8, 8)) },
                },
            },
            DestinationRect = new(0, 0, 16, 16, RelativeUnit.Absolute), TileMode = TileMode.Tile,
        };

        internal static Color ColorAt(int x, int y)
        {
            return ((x / 8 + y / 8) & 1) == 0 ? Light : Dark;
        }
    }
}

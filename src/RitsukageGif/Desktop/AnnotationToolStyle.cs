using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    public sealed record AnnotationToolStyle
    {
        public string Color { get; set; } = "#ffff0000";
        public double Thickness { get; set; } = 4;
        public double FontSize { get; set; } = 24;
        public int MosaicSize { get; set; } = 12;
        public ScreenshotAnnotationProperties Properties { get; set; } = new();

        public AnnotationToolStyle Normalize()
        {
            return this with
            {
                Color = Avalonia.Media.Color.TryParse(Color, out var color) ? color.ToString() : Colors.Red.ToString(),
                Thickness = double.IsFinite(Thickness) ? Math.Clamp(Thickness, 1, 1024) : 4,
                FontSize = double.IsFinite(FontSize) ? Math.Clamp(FontSize, 1, 1024) : 24,
                MosaicSize = Math.Clamp(MosaicSize, 2, 256),
                Properties = Properties.Normalize(),
            };
        }
    }
}

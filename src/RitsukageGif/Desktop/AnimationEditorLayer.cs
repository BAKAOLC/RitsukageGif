using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace RitsukageGif.Desktop
{
    internal sealed class AnimationEditorLayer
    {
        public string? Text { get; set; }
        public double FontSize { get; set; }
        public string? FontFamily { get; set; }
        public bool Bold { get; set; }
        public bool Italic { get; set; }
        public Color Color { get; set; }
        public FormattedText? Formatted { get; set; }
        public Bitmap? Image { get; init; }
        public Rect Bounds { get; set; }
        public double Opacity { get; set; } = 1;
    }
}

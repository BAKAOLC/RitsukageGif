using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace RitsukageGif.Desktop
{
    internal sealed class ScreenshotScrollingPreview : Control
    {
        public Bitmap? Image { get; set; }
        public PixelRect Viewport { get; set; }
        public PixelSize ImageSize { get; set; }
        public bool Matched { get; set; }

        public override void Render(DrawingContext context)
        {
            if (Image is not { } image || ImageSize.Width <= 0 || ImageSize.Height <= 0) return;
            var scale = Math.Min(Bounds.Width / ImageSize.Width, Bounds.Height / ImageSize.Height);
            var width = ImageSize.Width * scale;
            var height = ImageSize.Height * scale;
            var x = (Bounds.Width - width) / 2;
            var y = (Bounds.Height - height) / 2;
            var rectangle = new Rect(x, y, width, height);
            context.DrawImage(image, rectangle);
            if (!Matched) return;
            using var clip = context.PushClip(rectangle);
            context.DrawRectangle(null, new Pen(Brushes.LimeGreen, 2), new(x + Viewport.X * scale,
                y + Viewport.Y * scale, Viewport.Width * scale, Viewport.Height * scale));
        }
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotScrollingPreview : Decorator
    {
        private const double PreviewBorder = 4;
        private static readonly Color ViewportColor = Color.FromRgb(149, 236, 105);
        private static readonly Pen PreviewPen = new(AppTheme.Accent, PreviewBorder);
        private static readonly Pen ViewportPen = new(new ImmutableSolidColorBrush(ViewportColor), PreviewBorder);
        public Bitmap? Image { get; set; }
        public PixelRect Viewport { get; set; }
        public PixelSize ImageSize { get; set; }
        public bool Matched { get; set; }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            AppTheme.Accent.PropertyChanged += AccentChanged;
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            AppTheme.Accent.PropertyChanged -= AccentChanged;
            base.OnDetachedFromVisualTree(e);
        }

        private void AccentChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == SolidColorBrush.ColorProperty) InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            if (Image is not { } image || ImageSize.Width <= 0 || ImageSize.Height <= 0) return;
            var rectangle = ImageRectangle(Bounds.Size);
            if (rectangle.Width <= 0 || rectangle.Height <= 0) return;
            context.DrawImage(image, rectangle);
            context.DrawRectangle(null, PreviewPen, rectangle.Inflate(PreviewBorder / 2));
            if (!Matched) return;
            var scale = rectangle.Width / ImageSize.Width;
            context.DrawRectangle(null, ViewportPen, new Rect(rectangle.X + Viewport.X * scale,
                    rectangle.Y + Viewport.Y * scale, Viewport.Width * scale, Viewport.Height * scale)
                .Inflate(PreviewBorder / 2));
        }

        private Rect ImageRectangle(Size size)
        {
            var scale = Math.Max(0, Math.Min((size.Width - 2 * PreviewBorder) / ImageSize.Width,
                (size.Height - 2 * PreviewBorder) / ImageSize.Height));
            var width = ImageSize.Width * scale;
            var height = ImageSize.Height * scale;
            return new((size.Width - width) / 2, (size.Height - height) / 2, width, height);
        }
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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
        private ScrollCaptureAxis _axis;
        private bool _follow = true;
        private Vector _scroll;
        private double _zoom = 1;
        public Bitmap? Image { get; set; }
        public PixelRect Viewport { get; set; }
        public PixelSize ImageSize { get; set; }
        public bool Matched { get; set; }

        internal void UpdateView(ScrollCaptureAxis axis)
        {
            if (_axis != axis || ImageSize == default)
            {
                _axis = axis;
                _zoom = 1;
                _scroll = default;
                _follow = true;
            }
            else if (_follow && Matched && Bounds is { Width: > 0, Height: > 0 })
            {
                var rectangle = ImageRectangle(Bounds.Size);
                _scroll = new(Math.Max(0, -rectangle.X), Math.Max(0, -rectangle.Y));
            }

            InvalidateVisual();
            InvalidateArrange();
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            if (Image is null) return;
            var vertical = _axis == ScrollCaptureAxis.Vertical;
            var delta = e.Delta.Y != 0 ? e.Delta.Y : e.Delta.X;
            var old = ImageRectangle(Bounds.Size);
            _scroll = new(Math.Max(0, -old.X), Math.Max(0, -old.Y));
            _follow = false;
            if ((e.KeyModifiers & KeyModifiers.Control) != 0)
            {
                var position = e.GetPosition(this);
                var oldZoom = _zoom;
                _zoom = Math.Clamp(_zoom * Math.Pow(1.2, delta), .25, 3);
                _scroll = (position - old.Position) * (_zoom / oldZoom) - (Vector)position;
            }
            else
            {
                var scrollVertically = vertical ^ ((e.KeyModifiers & KeyModifiers.Shift) != 0);
                _scroll -= scrollVertically ? new(0, delta * 48) : new Vector(delta * 48, 0);
            }

            var rectangle = ImageRectangle(Bounds.Size);
            _scroll = new(Math.Max(0, -rectangle.X), Math.Max(0, -rectangle.Y));
            UpdateView(_axis);
            e.Handled = true;
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            if (e.ClickCount != 2 || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            _zoom = 1;
            _follow = true;
            UpdateView(_axis);
            e.Handled = true;
        }

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
            context.FillRectangle(Brushes.Transparent, new(Bounds.Size));
            var rectangle = ImageRectangle(Bounds.Size);
            if (rectangle.Width <= 0 || rectangle.Height <= 0) return;
            using var clip = context.PushClip(new(Bounds.Size));
            var visible = rectangle.Intersect(new(Bounds.Size));
            context.DrawImage(image, new(
                (visible.X - rectangle.X) * image.Size.Width / rectangle.Width,
                (visible.Y - rectangle.Y) * image.Size.Height / rectangle.Height,
                visible.Width * image.Size.Width / rectangle.Width,
                visible.Height * image.Size.Height / rectangle.Height), visible);
            context.DrawRectangle(null, PreviewPen, rectangle.Inflate(PreviewBorder / 2));
            if (!Matched) return;
            var scale = rectangle.Width / ImageSize.Width;
            context.DrawRectangle(null, ViewportPen, new Rect(rectangle.X + Viewport.X * scale,
                    rectangle.Y + Viewport.Y * scale, Viewport.Width * scale, Viewport.Height * scale)
                .Inflate(PreviewBorder / 2));
        }

        private Rect ImageRectangle(Size size)
        {
            var vertical = _axis == ScrollCaptureAxis.Vertical;
            var scale = Math.Max(0, Math.Min(1, vertical
                ? size.Width / ImageSize.Width
                : size.Height / ImageSize.Height)) * _zoom;
            var width = ImageSize.Width * scale;
            var height = ImageSize.Height * scale;
            var offset = _follow && Matched
                ? new((Viewport.X + Viewport.Width / 2d) * scale - size.Width / 2,
                    (Viewport.Y + Viewport.Height / 2d) * scale - size.Height / 2)
                : _scroll;
            return new(Start(width, size.Width, offset.X), Start(height, size.Height, offset.Y), width, height);
        }

        private static double Start(double extent, double viewport, double offset)
        {
            return extent <= viewport ? (viewport - extent) / 2 : -Math.Clamp(offset, 0, extent - viewport);
        }
    }
}

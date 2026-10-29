using Avalonia;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class OverlayCanvas
    {
        private PixelRect _decorationRegion;
        private double _decorationScale;
        private Rect _decorationViewport;
        private CombinedGeometry? _frameBorder;
        private RectangleGeometry? _frameClip;
        private CaptureDecoration _frameDecoration = CaptureDecoration.None;
        private IBrush _frameDecorationBrush = Brushes.Transparent;
        private CombinedGeometry? _frameOutside;
        internal SelectionDecorationPreview? FrameShadowPreview { get; set; }

        private double FrameRadius => Math.Min(_frameDecoration.CornerRadius,
            Math.Min(Selected.Width, Selected.Height) / 2.0) / Scale;

        internal void SetFrameDecoration(CaptureDecoration decoration)
        {
            if (_frameDecoration == decoration) return;
            _frameDecoration = decoration;
            _decorationScale = 0;
            InvalidateVisual();
        }

        private void PrepareFrameDecoration()
        {
            var viewport = new Rect(Bounds.Size);
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if (_decorationRegion == Selected && _decorationScale == Scale && _decorationViewport == viewport) return;
            _decorationRegion = Selected;
            _decorationScale = Scale;
            _decorationViewport = viewport;
            var local = Selected.ToLocal(_monitor, Scale);
            var region = new Rect(local.X, local.Y, local.Width, local.Height);
            _frameClip = new() { Rect = region, RadiusX = FrameRadius, RadiusY = FrameRadius };
            _frameOutside = new(GeometryCombineMode.Exclude, new RectangleGeometry(viewport), _frameClip);
            _frameDecorationBrush = new SolidColorBrush(Color.FromUInt32(_frameDecoration.Color));
            var width = _frameDecoration.Strength / Scale;
            _frameBorder = new(GeometryCombineMode.Exclude,
                new RectangleGeometry
                {
                    Rect = region.Inflate(width), RadiusX = FrameRadius + width, RadiusY = FrameRadius + width,
                }, _frameClip);
        }

        private void DrawFrameDecoration(DrawingContext context)
        {
            if (Selected.IsEmpty || _frameDecoration.Kind == CaptureDecorationKind.None || IsCandidate) return;
            PrepareFrameDecoration();
            if (_frameDecoration.Kind == CaptureDecorationKind.Border)
            {
                context.DrawGeometry(_frameDecorationBrush, null, _frameBorder!);
            }
            else if (FrameShadowPreview is { } preview)
            {
                var local = Selected.ToLocal(_monitor, Scale);
                using var outside = context.PushGeometryClip(_frameOutside!);
                context.DrawImage(preview.Image,
                    new(0, 0, preview.Image.PixelSize.Width, preview.Image.PixelSize.Height),
                    new(local.X - preview.ContentOffset.X / Scale, local.Y - preview.ContentOffset.Y / Scale,
                        preview.Image.PixelSize.Width / Scale, preview.Image.PixelSize.Height / Scale));
            }
        }
    }
}

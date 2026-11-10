using Avalonia.Controls;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed class AnnotationCurvePreview : Control
    {
        private static readonly Geometry Curve = IconCatalog.GetGeometry("AnnotationCurvePreviewCurve");
        private static readonly Geometry Straight = IconCatalog.GetGeometry("AnnotationCurvePreviewStraight");

        public AnnotationCurvePreview()
        {
            Width = 20;
            Height = 20;
        }

        public bool Curved { get; init; }
        public IBrush Stroke { get; set; } = AppTheme.Ink;

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            context.DrawGeometry(null, new Pen(Stroke, 1.5, lineCap: PenLineCap.Round),
                Curved ? Curve : Straight);
        }
    }
}

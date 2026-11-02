using Avalonia.Controls;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed class AnnotationCurvePreview : Control
    {
        private static readonly Geometry Curve = Geometry.Parse("M2,17 C4,1 16,1 18,17");
        private static readonly Geometry Straight = Geometry.Parse("M2,17 L18,3");

        public AnnotationCurvePreview()
        {
            Width = 20;
            Height = 20;
        }

        public bool Curved { get; set; }
        public IBrush Stroke { get; set; } = AppTheme.Ink;

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            context.DrawGeometry(null, new Pen(Stroke, 1.5, lineCap: PenLineCap.Round),
                Curved ? Curve : Straight);
        }
    }
}

using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        private IPen _selectionPen = SelectedAnnotationPen;
        private double _selectionPenScale = 1;

        internal double InteractionScale { get; set; } = 1;
        private double ControlHitRadius => 8 * InteractionScale;
        private double ControlHoverMargin => 10 * InteractionScale;

        private IPen SelectionPen(double scale)
        {
            if (_selectionPenScale.Equals(scale)) return _selectionPen;
            _selectionPenScale = scale;
            _selectionPen = scale.Equals(1d)
                ? SelectedAnnotationPen
                : new Pen(Brushes.DeepSkyBlue, scale, DashStyle.Dash);
            return _selectionPen;
        }
    }
}

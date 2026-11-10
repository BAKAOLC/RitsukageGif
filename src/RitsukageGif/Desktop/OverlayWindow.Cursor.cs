using Avalonia;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace RitsukageGif.Desktop
{
    internal sealed partial class OverlayWindow
    {
        private void UpdatePointerCursorScope(PointerEventArgs args)
        {
            var overControls = true;
            for (var visual = args.Source as Visual; visual is not null; visual = visual.GetVisualParent())
                if (ReferenceEquals(visual, _canvas) || ReferenceEquals(visual, _annotationSurface))
                {
                    overControls = false;
                    break;
                }

            if (_canvas.PointerOverCaptureControls == overControls) return;
            _canvas.PointerOverCaptureControls = overControls;
            InvalidateCanvas();
        }
    }
}

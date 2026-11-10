using Avalonia.Controls;
using Avalonia.Input;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        private AnnotationEndpointStyle _polylineArrowStart;
        private AnnotationEndpointStyle _polylineArrowEnd = AnnotationEndpointStyle.NotchedArrow;
        private Key? _polylineArrowKey;
        private readonly HashSet<Control> _polylineArrowTargets = [];

        internal bool CanSetPolylineArrows => !_disposed && CanAnnotate && !IsBusy && !IsEditingText &&
                                              (!IsDrawing || IsDrawingPolyline) &&
                                              Properties.StrokeStyle != AnnotationStrokeStyle.Filled &&
                                              (_gesture?.Tool ?? SelectedTool ?? Tool) is ScreenshotTool.Polyline
                                              or ScreenshotTool.Line;

        internal bool CanTogglePolylineArrows => CanSetPolylineArrows &&
                                                 _polylineArrowTargets.Any(static target =>
                                                     target is
                                                     {
                                                         IsEffectivelyVisible: true, IsEffectivelyEnabled: true,
                                                     });

        private bool PolylineArrowsEnabled => Properties.StartEndpoint != AnnotationEndpointStyle.None ||
                                              Properties.EndEndpoint != AnnotationEndpointStyle.None;

        internal void TrackPolylineArrowTarget(Control target, bool attached)
        {
            if (attached) _polylineArrowTargets.Add(target);
            else _polylineArrowTargets.Remove(target);
        }

        internal Task SetPolylineArrowsAsync(bool enabled)
        {
            if (!CanSetPolylineArrows || enabled == PolylineArrowsEnabled) return Task.CompletedTask;
            if (PolylineArrowsEnabled)
            {
                _polylineArrowStart = Properties.StartEndpoint;
                _polylineArrowEnd = Properties.EndEndpoint;
            }

            return UpdateSelectedPropertiesAsync(Properties with
            {
                StartEndpoint = enabled ? _polylineArrowStart : AnnotationEndpointStyle.None,
                EndEndpoint = enabled ? _polylineArrowEnd : AnnotationEndpointStyle.None,
            });
        }

        internal bool HandlePolylineArrowKey(Key key)
        {
            if (!CanTogglePolylineArrows) return false;
            if (_polylineArrowKey is not null) return true;
            _polylineArrowKey = key;
            _ = SetPolylineArrowsAsync(!PolylineArrowsEnabled);
            return true;
        }

        internal void ReleasePolylineArrowKey(Key? key = null)
        {
            if (key is null || _polylineArrowKey == key) _polylineArrowKey = null;
        }
    }
}

namespace RitsukageGif.Desktop
{
    internal sealed partial class SelectionCoordinator
    {
        private IReadOnlyList<WindowControlTarget>? _candidateHierarchy;
        private int _candidateIndex;
        private PixelPoint _candidatePoint;
        private nint _candidateWindow;

        // ReSharper disable once UnusedMethodReturnValue.Global
        internal bool CycleCandidate(PixelPoint point, bool parent)
        {
            if (_finished || _screenshotCompleting || !Controls || _state.Dragging ||
                !_state.Selected.IsEmpty || ScreenshotDrawing) return false;
            _pointer = point;
            UpdatePoint(true);
            if (_hoveredTarget is not { } target) return false;
            if (_candidateHierarchy is null || _candidatePoint != point || _candidateWindow != target.Handle)
            {
                _candidateHierarchy = _controlPoint == point && _controlWindow == target.Handle &&
                                      _controlHierarchy.Count > 0
                    ? _controlHierarchy
                    : WindowControlLocator.FindHierarchy(target, point, _cancel.Token);
                _candidatePoint = point;
                _candidateWindow = target.Handle;
                _candidateIndex = _candidateHierarchy.Count - 1;
            }

            _candidateIndex = Math.Clamp(_candidateIndex + (parent ? -1 : 1), 0, _candidateHierarchy.Count - 1);
            _hovered = ApplyDetectionMargin(_candidateHierarchy[_candidateIndex].Bounds
                .Intersect(WindowSource ? target.Client : target.Bounds));
            Refresh();
            return true;
        }

        private bool TryHierarchyCandidate(WindowTarget target, out PixelRect region)
        {
            region = default;
            if (_candidateHierarchy is null || _candidatePoint != _pointer || _candidateWindow != target.Handle)
            {
                _candidateHierarchy = null;
                return false;
            }

            region = _candidateHierarchy[_candidateIndex].Bounds
                .Intersect(WindowSource ? target.Client : target.Bounds);
            return true;
        }
    }
}

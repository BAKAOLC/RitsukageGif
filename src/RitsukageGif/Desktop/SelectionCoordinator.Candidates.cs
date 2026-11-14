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
            if (_finished || _screenshotCompleting || !CanDetectHoverControl || _state.Dragging ||
                !_state.Selected.IsEmpty || ScreenshotDrawing) return false;
            _pointer = point;
            UpdatePoint(true);
            if (_hoveredTarget is not { } target) return false;
            if (_candidateHierarchy is null || _candidatePoint != point || _candidateWindow != target.Handle)
            {
                var hierarchy = _controlPoint == point && _controlWindow == target.Handle &&
                                _controlHierarchy.Count > 0
                    ? _controlHierarchy
                    : OperatingSystem.IsWindows()
                        ? WindowControlLocator.FindHierarchy(target, point, _cancel.Token)
                        : [];
                if (hierarchy.Count == 0) return false;
                _candidateHierarchy = hierarchy;
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

        private void UpdateCandidateHierarchy(WindowTarget target, PixelPoint point,
            IReadOnlyList<WindowControlTarget> hierarchy)
        {
            if (_candidateHierarchy is null || _candidatePoint != point || _candidateWindow != target.Handle) return;
            var selected = _candidateHierarchy[_candidateIndex];
            var index = hierarchy.Count - 1;
            for (var item = 0; item < hierarchy.Count; item++)
                if (hierarchy[item].Handle == selected.Handle && hierarchy[item].Bounds == selected.Bounds)
                {
                    index = item;
                    break;
                }

            _candidateHierarchy = hierarchy.Count == 0 ? null : hierarchy;
            _candidateIndex = index;
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

namespace RitsukageGif.Desktop
{
    internal sealed partial class SelectionCoordinator
    {
        private long _lastScreenshotWheel = Environment.TickCount64 - 50;
        private PixelRect _wheelCandidateBasis;
        private int _wheelCandidateInset;

        private PixelRect ApplyMultiWindowCandidateWheel(PixelRect candidate)
        {
            if (ShapeMode != SelectionShapeMode.MultiWindow)
            {
                _wheelCandidateBasis = default;
                _wheelCandidateInset = 0;
                return candidate;
            }

            if (_wheelCandidateBasis != candidate)
            {
                _wheelCandidateBasis = candidate;
                _wheelCandidateInset = 0;
            }

            return _wheelCandidateInset == 0 ? candidate : ResizeWheelCandidate(_wheelCandidateInset);
        }

        private void ResizeMultiWindowCandidate(PixelPoint point, int direction)
        {
            if (UsesLocalSource) return;
            _pointer = point;
            UpdatePoint();
            if (_wheelCandidateBasis.IsEmpty || _hoveredTarget is not { } target) return;
            var inset = _wheelCandidateInset + direction;
            var next = ResizeWheelCandidate(inset);
            var limits = WindowSource ? target.Client : SelectionBounds();
            if (next.Width < 5 || next.Height < 5 || next.Intersect(limits) != next) return;
            _wheelCandidateInset = inset;
            _hovered = next;
            Refresh();
        }

        private PixelRect ResizeWheelCandidate(int inset)
        {
            return new(_wheelCandidateBasis.X - inset, _wheelCandidateBasis.Y - inset,
                _wheelCandidateBasis.Width + inset * 2, _wheelCandidateBasis.Height + inset * 2);
        }

        internal void ScreenshotWheel(PixelPoint point, double delta)
        {
            if (!ScreenshotMode || _finished || _screenshotCompleting || _state.Dragging ||
                SelectionManipulating || ScreenshotDrawing || CapturePreferences.DisableMouseWheel ||
                delta == 0 || Annotations is { IsBusy: true }) return;
            var now = Environment.TickCount64;
            if (now - _lastScreenshotWheel < 50) return;
            _lastScreenshotWheel = now;
            var functions = CapturePreferences.WheelFunctions;
            if (_state.Selected.IsEmpty)
            {
                if (ShapeMode == SelectionShapeMode.MultiWindow &&
                    (functions & CaptureWheelFunctions.ResizeSelection) != 0 && !GeometrySizeFixed)
                    ResizeMultiWindowCandidate(point, Math.Sign(delta));
                else if ((functions & CaptureWheelFunctions.CandidateHierarchy) != 0)
                    CycleCandidate(point, delta > 0);
                return;
            }

            if ((functions & CaptureWheelFunctions.ResizeSelection) == 0 || GeometrySizeFixed) return;
            var region = _state.Selected;
            var direction = Math.Sign(delta);
            var next = new PixelRect(region.X - direction, region.Y - direction,
                region.Width + direction * 2, region.Height + direction * 2);
            var limits = WindowSource && !UsesLocalSource && ShapeMode != SelectionShapeMode.MultiWindow
                ? Snapshot.Target?.Client ?? default
                : SelectionBounds();
            if (next.Width < 5 || next.Height < 5 || next.Intersect(limits) != next) return;
            ChangeGeometry(next.X, next.Y, next.Width, next.Height, true);
        }
    }
}

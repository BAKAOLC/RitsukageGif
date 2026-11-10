namespace RitsukageGif.Desktop
{
    internal sealed partial class SelectionCoordinator
    {
        private long _lastScreenshotWheel = Environment.TickCount64 - 50;

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
                if ((functions & CaptureWheelFunctions.CandidateHierarchy) != 0) CycleCandidate(point, delta > 0);
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

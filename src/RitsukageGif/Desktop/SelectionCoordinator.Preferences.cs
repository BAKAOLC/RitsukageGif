namespace RitsukageGif.Desktop
{
    internal sealed partial class SelectionCoordinator
    {
        private PixelRect _expandFrom;
        private WindowTarget? _expandTarget;
        internal int SnappingMode { get; private set; } = 3;
        internal CapturePreferences CapturePreferences { get; private set; } = new();
        internal CaptureDetectionMode DetectionMode { get; private set; } = CaptureDetectionMode.Windows;
        internal bool AuxiliaryVisible { get; private set; }

        private void PrepareOutsideExpansion()
        {
            _expandFrom = default;
            _expandTarget = null;
            if (!CapturePreferences.ExpandOnOutsideClick || _state.Selected.IsEmpty ||
                _state.Selected.Contains(_pointer)) return;
            _expandFrom = _state.Selected;
            _expandTarget = _selectedTarget;
        }

        private void CompleteOutsideExpansion(bool dragged)
        {
            var original = _expandFrom;
            _expandFrom = default;
            if (original.IsEmpty || dragged) return;
            var left = Math.Min(original.X, _pointer.X);
            var top = Math.Min(original.Y, _pointer.Y);
            var right = Math.Max(original.Right, (long)_pointer.X + 1);
            var bottom = Math.Max(original.Bottom, (long)_pointer.Y + 1);
            var limits = WindowSource ? _expandTarget?.Client ?? default : SelectionBounds();
            left = Math.Max(left, limits.X);
            top = Math.Max(top, limits.Y);
            right = Math.Min(right, limits.Right);
            bottom = Math.Min(bottom, limits.Bottom);
            if (right <= left || bottom <= top) return;
            _state.Set(new(left, top, (int)(right - left), (int)(bottom - top)));
            _selectedTarget = _expandTarget;
            _selectionIsWholeTarget = false;
        }

        internal event Action? PreferencesChanged;

        internal void InitializePreferences(bool detectControls, int snappingMode, SelectionHintMode hints,
            CapturePreferences? capturePreferences = null)
        {
            if (_started) throw new InvalidOperationException();
            Controls = detectControls && CanDetectControls;
            SnappingMode = Math.Clamp(snappingMode, 0, 3);
            HintMode = Enum.IsDefined(hints) ? hints : SelectionHintMode.Full;
            if (capturePreferences is null) return;
            CapturePreferences = capturePreferences.Normalize();
            DetectionMode = CapturePreferences.DetectionMode;
            Controls = DetectionMode == CaptureDetectionMode.Controls && CanDetectControls;
            _cursor = CapturePreferences.CursorPolicy == CaptureCursorPolicy.Visible;
            SampleColorFormat = (SampleColorFormat)CapturePreferences.MagnifierColorFormat;
        }

        private PixelRect ApplyDetectionMargin(PixelRect region)
        {
            if (region.IsEmpty || CapturePreferences.ElementMargin == 0) return region;
            var margin = CapturePreferences.ElementMargin;
            var bounds = SelectionBounds();
            var left = Math.Max(bounds.X, (long)region.X - margin);
            var top = Math.Max(bounds.Y, (long)region.Y - margin);
            var right = Math.Min(bounds.Right, (long)region.Right + margin);
            var bottom = Math.Min(bounds.Bottom, (long)region.Bottom + margin);
            return right <= left || bottom <= top
                ? default
                : new((int)left, (int)top,
                    (int)(right - left), (int)(bottom - top));
        }
    }
}

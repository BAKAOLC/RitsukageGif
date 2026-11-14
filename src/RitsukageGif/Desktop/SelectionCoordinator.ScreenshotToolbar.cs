using Avalonia;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SelectionCoordinator
    {
        private OverlayWindow? _screenshotToolbarCapture;
        private Point _screenshotToolbarPress;
        private OverlayWindow? _manualScreenshotToolbarOwner;
        private (Point Position, double Scale, Size Viewport, bool Above) _screenshotToolbarOrigin;

        internal (Point Position, double Scale, Size Viewport, bool Above)? ManualScreenshotToolbar
        {
            get;
            private set;
        }

        internal bool ScreenshotToolbarDragInProgress => _screenshotToolbarCapture is not null;

        internal bool DesktopScreenshotToolbarVisible =>
            Snapshot.Phase is SelectionPhase.Selected or SelectionPhase.Confirming;

        internal bool OwnsDesktopScreenshotToolbar(OverlayWindow overlay, PixelRect selected)
        {
            if (!DesktopScreenshotToolbarVisible || selected.IsEmpty)
                return false;
            if ((_screenshotToolbarCapture ?? _manualScreenshotToolbarOwner) is { } manual)
                return ReferenceEquals(manual, overlay);
            var center = new PixelPoint(selected.X + selected.Width / 2, selected.Y + selected.Height / 2);
            var owner = _windows.FirstOrDefault(window => window.Region.Contains(center)) ??
                        _windows.FirstOrDefault(window => !window.Region.Intersect(selected).IsEmpty);
            return ReferenceEquals(owner, overlay);
        }

        internal void ActivateDesktopScreenshotToolbar(OverlayWindow overlay)
        {
            if (ManualScreenshotToolbar is null || _screenshotToolbarCapture is not null ||
                ReferenceEquals(_manualScreenshotToolbarOwner, overlay)) return;
            _manualScreenshotToolbarOwner?.HideDesktopScreenshotPanels();
            _manualScreenshotToolbarOwner = overlay;
            RefreshDesktopScreenshotToolbar(true);
        }

        internal void BeginScreenshotToolbarDrag(OverlayWindow overlay, ScreenshotToolbar toolbar, Point pointer)
        {
            _screenshotToolbarCapture = overlay;
            overlay.HideDesktopScreenshotPanels();
            _screenshotToolbarPress = pointer;
            var scale = overlay.ScreenshotToolbarScale;
            _screenshotToolbarOrigin = ManualScreenshotToolbar ??
                                       (toolbar.DesktopPosition, scale,
                                           new(overlay.Region.Width / scale, overlay.Region.Height / scale),
                                           toolbar.DesktopAbove);
        }

        internal void MoveScreenshotToolbar(OverlayWindow overlay, Point pointer)
        {
            if (!ReferenceEquals(_screenshotToolbarCapture, overlay) || (pointer == _screenshotToolbarPress &&
                                                                         ManualScreenshotToolbar is null)) return;
            var position = _screenshotToolbarOrigin.Position + (pointer - _screenshotToolbarPress);
            if (ManualScreenshotToolbar is { } current && current.Position == position) return;
            var positionOnly = ManualScreenshotToolbar is not null;
            var origin = _screenshotToolbarOrigin;
            ManualScreenshotToolbar = (position, origin.Scale, origin.Viewport, origin.Above);
            _manualScreenshotToolbarOwner = _windows.FirstOrDefault(window =>
                pointer.X >= window.Region.X && pointer.X < window.Region.Right &&
                pointer.Y >= window.Region.Y && pointer.Y < window.Region.Bottom) ?? overlay;
            RefreshDesktopScreenshotToolbar(positionOnly);
        }

        internal void EndScreenshotToolbarDrag(OverlayWindow overlay)
        {
            if (!ReferenceEquals(_screenshotToolbarCapture, overlay)) return;
            _screenshotToolbarCapture = null;
            if (ManualScreenshotToolbar is not null) RefreshDesktopScreenshotToolbar();
        }

        internal void ResetScreenshotToolbar()
        {
            if (ManualScreenshotToolbar is null) return;
            _screenshotToolbarCapture = null;
            _manualScreenshotToolbarOwner = null;
            ManualScreenshotToolbar = null;
            RefreshDesktopScreenshotToolbar();
        }

        private void RefreshDesktopScreenshotToolbar(bool positionOnly = false)
        {
            foreach (var window in _windows) window.RefreshDesktopScreenshotToolbar(positionOnly);
        }
    }
}

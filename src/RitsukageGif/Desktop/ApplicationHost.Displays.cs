namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private void DisplayChanged(object? sender, EventArgs args)
        {
            WindowsDisplayColor.Invalidate();
            RefreshDisplaySelection();
        }

        private void RefreshDisplaySelection()
        {
            if (IsClosing || IsBusy || IsSelecting || DesktopCapture.UsesSourceSelection ||
                _presets.ActivePreset is not null || _manualPreset is not null || Region is not { } region)
                return;
            if (Screens.All.Any(screen => !region.Intersect(new(screen.Bounds.X, screen.Bounds.Y,
                    screen.Bounds.Width, screen.Bounds.Height)).IsEmpty)) return;
            Region = null;
            Volatile.Write(ref _captureRegion, null);
            SetStatusKey("TopologyChanged");
        }
    }
}

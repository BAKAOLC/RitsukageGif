namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private void DisplayChanged(object? sender, EventArgs args)
        {
            RefreshDisplaySelection();
        }

        private void RefreshDisplaySelection()
        {
            if (_closing || _busy || _selecting || DesktopCapture.UsesSourceSelection ||
                _presets.ActivePreset is not null || _manualPreset is not null || _region is not { } region)
                return;
            if (Screens.All.Any(screen => !region.Intersect(new PixelRect(screen.Bounds.X, screen.Bounds.Y,
                    screen.Bounds.Width, screen.Bounds.Height)).IsEmpty)) return;
            _region = null;
            Volatile.Write(ref _captureRegion, null);
            SetStatusKey("TopologyChanged");
        }
    }
}

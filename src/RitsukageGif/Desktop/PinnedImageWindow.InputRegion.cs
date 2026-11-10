namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        private PinnedInputRegionController? _inputRegions;

        private bool CanIgnoreTransparentInput => _inputRegions is not null;

        private void InitializeInputRegions()
        {
            var backend = PinnedInputRegionFactory.TryCreate(this, () => IsMouseThrough);
            if (backend is null) return;
            _inputRegions = new(this, () => !LiveCaptureActive && !AnimationActive,
                () => _hdrPreview is { IsVisible: true } host ? PinnedInputGeometry.ViewportBounds(host, this) : null,
                RefreshHdrPreview, backend,
                error => _ = RunAsync(() => Task.FromException(error)));
        }

        private void InvalidateInputUi()
        {
            _inputRegions?.InvalidateUi();
        }

        private PinnedViewportSnapshot SnapshotInputHdrViewport()
        {
            var viewport = SnapshotImageViewport() with { Opacity = 1 };
            if (!OperatingSystem.IsWindows() || _hdrPreview is not { } host ||
                PinnedInputGeometry.ViewportBounds(host, this) is not { } bounds) return viewport;
            return viewport with
            {
                TargetSize = new(bounds.Width, bounds.Height),
                Clip = viewport.Clip is { } clip
                    ? clip with { TargetClip = clip.TargetClip.Intersect(new(0, 0, bounds.Width, bounds.Height)) }
                    : null,
            };
        }
    }
}

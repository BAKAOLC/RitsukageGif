namespace RitsukageGif.Desktop
{
    internal sealed class PinnedX11InputRegion(nint window, Func<bool> mouseThrough) : IPinnedInputRegionBackend
    {
        private PinnedInputRegion? _applied;
        private bool _through, _disposed, _ownsShape;

        public bool ClipsVisualOutput => false;

        public void Apply(PinnedInputRegion coverage)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var through = mouseThrough();
            if (_through == through && _applied?.HasSameShape(coverage) == true) return;
            _ownsShape = true;
            if (!X11WindowSystem.SetInputRegion(window, coverage, through))
            {
                _ownsShape = false;
                _applied = null;
                return;
            }

            _applied = coverage;
            _through = through;
        }

        public void Reset()
        {
            if (!_ownsShape) return;
            X11WindowSystem.SetInputRegion(window, null, mouseThrough());
            _applied = null;
            _ownsShape = false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            Reset();
            _disposed = true;
        }
    }
}

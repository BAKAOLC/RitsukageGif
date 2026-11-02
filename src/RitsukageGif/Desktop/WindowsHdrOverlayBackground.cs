namespace RitsukageGif.Desktop
{
    internal sealed class WindowsHdrOverlayBackground : IHdrOverlayBackground
    {
        private readonly HdrOverlayWindow _window;
        private readonly nint _overlay;
        private readonly PixelRect _bounds;

        internal WindowsHdrOverlayBackground(nint overlay, PixelRect bounds)
        {
            _overlay = overlay;
            _bounds = bounds;
            _window = new(overlay, bounds);
        }

        public event Action? DisplayChanged
        {
            add => _window.DisplayChanged += value;
            remove => _window.DisplayChanged -= value;
        }

        public bool SupportsHdr => WindowsDisplayColor.Read(WindowsDisplayColor.ForRegion(_bounds)).Hdr;
        public bool SupportsTransparentInput => HdrOverlayWindow.SupportsTransparentInput(_overlay);

        public void InvalidateDevice()
        {
            _window.InvalidateDevice();
        }

        public Task PresentAsync(CapturedFrame frame, CapturedFrame? windowFrame, PixelRect windowBounds,
            CancellationToken token)
        {
            return _window.PresentAsync(frame, windowFrame, windowBounds, token);
        }

        public void SetSelection(PixelRect selection)
        {
            _window.SetSelection(selection);
        }

        public void Show()
        {
            _window.Show();
        }

        public void Hide()
        {
            _window.Hide();
        }

        public void Dispose()
        {
            _window.Dispose();
        }
    }
}

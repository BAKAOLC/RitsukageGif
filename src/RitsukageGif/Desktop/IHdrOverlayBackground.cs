namespace RitsukageGif.Desktop
{
    internal interface IHdrOverlayBackground : IDisposable
    {
        bool SupportsHdr { get; }
        bool SupportsTransparentInput { get; }
        event Action? DisplayChanged;
        void InvalidateDevice();

        Task PresentAsync(CapturedFrame frame, CapturedFrame? windowFrame, PixelRect windowBounds,
            CancellationToken token);

        void SetSelection(PixelRect selection);
        void Show();
        void Hide();
    }
}

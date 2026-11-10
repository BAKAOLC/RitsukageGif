namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        private bool _animationActiveAtClose;
        internal ReadOnlyMemory<byte> CurrentPng => _png;
        internal PinnedImageOriginal OriginalContent { get; private set; } = null!;

        internal bool StoredAnimationActive => _closed ? _animationActiveAtClose : AnimationActive;

        private void InitializeOriginal(PinnedImageContent content)
        {
            var hdr = content.Hdr is { } source
                ? source with { ColorRegions = Array.AsReadOnly([.. source.ColorRegions]) }
                : null;
            OriginalContent = new([.. content.Png], content.SdrPng?.ToArray(), hdr)
                { Encoded = content.OriginalEncoded, Animation = content.Animation };
            _png = OriginalContent.Png;
            _sdrPng = OriginalContent.SdrPng;
            _hdrSource = hdr;
        }

        internal void RestoreOriginal(PinnedImageOriginal original, bool sameAsCurrent = false)
        {
            OriginalContent = sameAsCurrent ? original with { Png = _png } : original;
        }

        private Task<ScreenshotResult> CapturePreferredOutputAsync(OutputColorMode mode, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_closed, this);
            return CurrentPreferences.OutputContent == PinnedImageOutputContent.Original
                ? CaptureSceneOutputAsync(mode, token)
                : CaptureOutputAsync(mode, token);
        }
    }
}

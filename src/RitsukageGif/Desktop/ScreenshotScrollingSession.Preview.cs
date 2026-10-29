namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotScrollingSession
    {
        private int _previewMaximumHeight = axis == ScrollCaptureAxis.Vertical ? 1600 : 400;
        private int _previewMaximumWidth = axis == ScrollCaptureAxis.Vertical ? 400 : 1600;

        internal void ConfigurePreviewSize(int maxWidth, int maxHeight)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxWidth);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxHeight);
            maxWidth = Math.Min(maxWidth, 4096);
            maxHeight = Math.Min(maxHeight, 4096);
            var scale = Math.Min(1, Math.Sqrt(1048576d / ((long)maxWidth * maxHeight)));
            maxWidth = Math.Max(1, (int)(maxWidth * scale));
            maxHeight = Math.Max(1, (int)(maxHeight * scale));
            lock (_sync)
            {
                if (_previewMaximumWidth == maxWidth && _previewMaximumHeight == maxHeight) return;
                _previewMaximumWidth = maxWidth;
                _previewMaximumHeight = maxHeight;
                _previewDirty = true;
            }
        }
    }
}

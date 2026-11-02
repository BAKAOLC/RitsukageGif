namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotScrollingSession
    {
        internal bool AutoCropOnReverse
        {
            get
            {
                lock (_sync) return _stitching.AutoCropOnReverse;
            }
            set
            {
                lock (_sync) _stitching.AutoCropOnReverse = value;
            }
        }
    }
}

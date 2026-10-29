namespace RitsukageGif.Desktop
{
    [Flags]
    public enum ScreenshotAutoSaveTriggers
    {
        None = 0,
        Copy = 1,
        Pin = 2,
        Recognize = 4,
        Save = 8,
        Scrolling = 16,
        All = Copy | Pin | Recognize | Save | Scrolling,
    }
}

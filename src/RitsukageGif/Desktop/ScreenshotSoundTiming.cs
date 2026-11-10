namespace RitsukageGif.Desktop
{
    [Flags]
    public enum ScreenshotSoundTiming
    {
        None = 0,
        Start = 1,
        Pin = 2,
        Save = 4,
        QuickSave = 8,
        Copy = 16,
        All = Start | Pin | Save | QuickSave | Copy,
    }
}

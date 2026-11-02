namespace RitsukageGif.Desktop
{
    [Flags]
    public enum CaptureBlockedWindowsKeys
    {
        None = 0,
        Left = 1,
        Right = 2,
        All = Left | Right,
    }
}

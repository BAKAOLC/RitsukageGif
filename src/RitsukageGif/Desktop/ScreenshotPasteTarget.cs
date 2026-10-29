namespace RitsukageGif.Desktop
{
    internal sealed record ScreenshotPasteTarget(
        nint Window,
        nint Focus,
        uint ProcessId,
        uint ThreadId,
        WindowsFocusedElementIdentity Identity);
}

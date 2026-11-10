namespace RitsukageGif.Desktop
{
    internal sealed record GlobalActionDefinition(
        GlobalActionKind Kind,
        string IconKey,
        ActionScreenshotOutput? ScreenshotOutput = null,
        bool DirectScreenshot = false,
        bool FileParameter = false,
        bool StartWithScrolling = false,
        bool AutoConfirm = true,
        bool StartWithRecording = false)
    {
        internal string NameKey => "GlobalAction" + Kind;
        internal bool IsScreenshot => ScreenshotOutput is not null;
    }
}

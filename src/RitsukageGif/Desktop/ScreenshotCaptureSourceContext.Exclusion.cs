namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class ScreenshotCaptureSourceContext
    {
        private readonly NativeCaptureSessionReference? _portalSourceReference;

        internal string? CursorUnavailableReason => _portalSourceReference?.Session.CursorUnavailableReason;

        internal NativeCaptureCursorFrame? GetCursorFrame()
        {
            return _portalSourceReference?.Session.ReadCursor();
        }
    }
}

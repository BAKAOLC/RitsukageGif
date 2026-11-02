namespace RitsukageGif.Desktop
{
    internal sealed record NativeCaptureSnapshot(
        CapturedFrameSet Frames,
        NativeCaptureGeometry Geometry,
        NativeCaptureCursor? Cursor,
        byte[]? CursorPixels,
        NativeCaptureCapabilities? Capabilities = null,
        bool CursorEmbedded = false);
}

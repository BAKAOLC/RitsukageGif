namespace RitsukageGif.Desktop
{
    internal sealed record FrozenCaptureResult(
        byte[] Pixels,
        CapturedFrame? Hdr,
        CapturedFrameSet? NativeFrames = null);
}

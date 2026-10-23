namespace RitsukageGif.Desktop
{
    internal delegate void CaptureFrameConsumer(CaptureFrameHeader frame, ReadOnlySpan<byte> pixels);
}

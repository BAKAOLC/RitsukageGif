namespace RitsukageGif.Core
{
    public sealed partial class ScrollCaptureSession
    {
        private sealed record Strip(int Start, int End, byte[] Pixels, int DataOffset = 0);
    }
}

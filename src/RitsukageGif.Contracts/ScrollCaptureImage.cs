namespace RitsukageGif.Contracts
{
    public sealed record ScrollCaptureImage(byte[] Pixels, int Width, int Height)
    {
        public CapturedFrame? Hdr { get; init; }
    }
}

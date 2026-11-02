namespace RitsukageGif.Desktop
{
    internal enum NativeCapturePacketKind : byte
    {
        Frame = 1,
        Reply = 2,
        Capabilities = 3,
        Cursor = 4,
    }
}

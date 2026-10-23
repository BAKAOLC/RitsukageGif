namespace RitsukageGif.Desktop
{
    internal sealed record WindowTarget(nint Handle, string Process, string Title, PixelRect Bounds, PixelRect Client);
}

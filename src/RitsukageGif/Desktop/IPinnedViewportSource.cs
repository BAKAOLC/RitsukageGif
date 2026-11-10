namespace RitsukageGif.Desktop
{
    internal interface IPinnedViewportSource
    {
        int Width { get; }
        int Height { get; }
        PinnedViewportSample Read(int x, int y);
    }
}

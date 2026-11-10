namespace RitsukageGif.Desktop
{
    internal interface IPinnedViewportTarget
    {
        void Write(int pixel, PinnedViewportSample sample, double opacity);
    }
}

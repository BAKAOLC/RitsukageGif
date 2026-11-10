namespace RitsukageGif.Desktop
{
    // ReSharper disable once StructCanBeMadeReadOnly
    internal struct PinnedViewportClipTarget<TTarget>(
        TTarget target,
        int width,
        PinnedViewportCoverage coverage) : IPinnedViewportTarget where TTarget : struct, IPinnedViewportTarget
    {
        private TTarget _target = target;

        public void Write(int pixel, PinnedViewportSample sample, double opacity)
        {
            _target.Write(pixel, sample, opacity * coverage.At(pixel % width, pixel / width));
        }
    }
}

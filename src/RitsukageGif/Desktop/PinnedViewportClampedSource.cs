namespace RitsukageGif.Desktop
{
    // ReSharper disable once StructCanBeMadeReadOnly
    internal struct PinnedViewportClampedSource<TSource>(TSource source, int padding) : IPinnedViewportSource
        where TSource : struct, IPinnedViewportSource
    {
        private TSource _source = source;

        public int Width => _source.Width + padding * 2;
        public int Height => _source.Height + padding * 2;

        public PinnedViewportSample Read(int x, int y)
        {
            return _source.Read(Math.Clamp(x - padding, 0, _source.Width - 1),
                Math.Clamp(y - padding, 0, _source.Height - 1));
        }
    }
}

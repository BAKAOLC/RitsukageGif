namespace RitsukageGif.Desktop
{
    internal readonly struct PinnedViewportKernel
    {
        private readonly double _center;
        private readonly double _footprint;

        internal PinnedViewportKernel(double center, double footprint, int length)
        {
            _center = center;
            _footprint = footprint;
            var first = footprint > 1 ? Math.Floor(center - footprint / 2) : Math.Floor(center - .5);
            var last = footprint > 1 ? Math.Ceiling(center + footprint / 2) - 1 : first + 1;
            First = (int)Math.Clamp(first, 0, length);
            Last = (int)Math.Clamp(last, -1, length - 1);
        }

        internal int First { get; }
        internal int Last { get; }

        internal double Weight(int pixel)
        {
            return _footprint > 1
                ? Math.Max(0,
                      Math.Min(pixel + 1, _center + _footprint / 2) - Math.Max(pixel, _center - _footprint / 2)) /
                  _footprint
                : Math.Max(0, 1 - Math.Abs(pixel + .5 - _center));
        }
    }
}

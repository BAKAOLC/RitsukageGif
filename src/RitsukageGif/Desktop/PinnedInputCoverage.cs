using Avalonia;

namespace RitsukageGif.Desktop
{
    internal sealed class PinnedInputCoverage(PixelSize size)
    {
        private readonly ulong[] _bits = new ulong[checked((int)(((long)size.Width * size.Height + 63) / 64))];

        internal PixelSize Size { get; } = size;

        internal bool Contains(int x, int y)
        {
            var pixel = (long)y * Size.Width + x;
            return (_bits[pixel / 64] & (1UL << (int)(pixel % 64))) != 0;
        }

        internal void Include(int x, int y)
        {
            var pixel = (long)y * Size.Width + x;
            _bits[pixel / 64] |= 1UL << (int)(pixel % 64);
        }
    }
}

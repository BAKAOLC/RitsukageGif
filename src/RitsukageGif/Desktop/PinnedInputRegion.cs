using Avalonia;

namespace RitsukageGif.Desktop
{
    internal sealed class PinnedInputRegion(PixelSize size, PixelRect[] rectangles)
    {
        internal PixelSize Size { get; } = size;
        internal ReadOnlySpan<PixelRect> Rectangles => rectangles;

        internal bool HasSameShape(PinnedInputRegion other)
        {
            return Size == other.Size && Rectangles.SequenceEqual(other.Rectangles);
        }
    }
}

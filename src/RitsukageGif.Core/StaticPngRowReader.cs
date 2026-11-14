namespace RitsukageGif.Core
{
    public delegate void StaticPngRowReader(int row, Span<byte> pixels, CancellationToken token);
}

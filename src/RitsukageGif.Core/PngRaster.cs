namespace RitsukageGif.Core
{
    public sealed record PngRaster(
        int Width,
        int Height,
        byte Depth,
        byte ColorType,
        byte[] Samples,
        IReadOnlyList<(string Type, ReadOnlyMemory<byte> Data)> Metadata);
}

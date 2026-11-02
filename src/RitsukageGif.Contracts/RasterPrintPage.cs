namespace RitsukageGif.Contracts
{
    public sealed record RasterPrintPage(ReadOnlyMemory<byte> Png, double Dpi, double WidthPoints, double HeightPoints);
}

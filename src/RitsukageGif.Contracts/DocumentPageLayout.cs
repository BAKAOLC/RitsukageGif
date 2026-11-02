namespace RitsukageGif.Contracts
{
    public sealed record DocumentPageLayout(
        double WidthPoints,
        double HeightPoints,
        double SourceX,
        double SourceY,
        double SourceWidth,
        double SourceHeight,
        double DestinationX,
        double DestinationY,
        double DestinationWidth,
        double DestinationHeight);
}

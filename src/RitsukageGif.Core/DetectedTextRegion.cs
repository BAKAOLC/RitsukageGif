namespace RitsukageGif.Core
{
    internal readonly record struct DetectedTextRegion(
        double CenterX,
        double CenterY,
        double Width,
        double Height,
        double Angle,
        double ContentWidth);
}

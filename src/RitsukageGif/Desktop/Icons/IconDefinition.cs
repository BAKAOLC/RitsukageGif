namespace RitsukageGif.Desktop.Icons
{
    internal sealed class IconDefinition(
        double minX,
        double minY,
        double width,
        double height,
        SvgPathDefinition[] paths)
    {
        internal double MinX { get; } = minX;
        internal double MinY { get; } = minY;
        internal double Width { get; } = width;
        internal double Height { get; } = height;
        internal SvgPathDefinition[] Paths { get; } = paths;
    }
}

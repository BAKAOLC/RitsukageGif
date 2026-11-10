namespace RitsukageGif.Desktop.Icons
{
    internal sealed class SvgPathDefinition(
        string data,
        bool fill,
        bool stroke,
        double strokeWidth,
        string lineCap,
        string lineJoin,
        string fillRule)
    {
        internal string Data { get; } = data;
        internal bool Fill { get; } = fill;
        internal bool Stroke { get; } = stroke;
        internal double StrokeWidth { get; } = strokeWidth;
        internal string LineCap { get; } = lineCap;
        internal string LineJoin { get; } = lineJoin;
        internal string FillRule { get; } = fillRule;
    }
}

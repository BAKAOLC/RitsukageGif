using Avalonia.Media;

namespace RitsukageGif.Desktop.Icons
{
    internal sealed class CachedSvgPath
    {
        internal CachedSvgPath(SvgPathDefinition definition)
        {
            Definition = definition;
            Geometry = Geometry.Parse((definition.FillRule == "nonzero" ? "F1 " : "F0 ") + definition.Data);
            Stroke = definition.Stroke ? CreatePen(Brushes.Black) : null;
        }

        internal SvgPathDefinition Definition { get; }
        internal Geometry Geometry { get; }
        internal IPen? Stroke { get; }

        internal Pen CreatePen(IBrush? brush)
        {
            return new(brush, Definition.StrokeWidth, lineCap: Definition.LineCap switch
            {
                "round" => PenLineCap.Round,
                "square" => PenLineCap.Square,
                _ => PenLineCap.Flat,
            }, lineJoin: Definition.LineJoin switch
            {
                "round" => PenLineJoin.Round,
                "bevel" => PenLineJoin.Bevel,
                _ => PenLineJoin.Miter,
            }, miterLimit: 4);
        }
    }
}

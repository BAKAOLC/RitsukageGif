using Avalonia;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed record AnnotationStrokeGeometry(Geometry Path, IPen Pen, Rect Clip);
}

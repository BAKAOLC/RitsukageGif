using Avalonia;

namespace RitsukageGif.Desktop
{
    internal readonly record struct AnnotationDocumentRegion(double X, double Y, double Width, double Height)
    {
        internal Rect Bounds => new(X, Y, Width, Height);

        internal bool IsValid => double.IsFinite(X) && double.IsFinite(Y) &&
                                 double.IsFinite(Width) && double.IsFinite(Height) && Width >= 0 && Height >= 0;
    }
}

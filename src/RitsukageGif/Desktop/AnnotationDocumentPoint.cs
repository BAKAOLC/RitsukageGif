using Avalonia;

namespace RitsukageGif.Desktop
{
    internal readonly record struct AnnotationDocumentPoint(double X, double Y)
    {
        internal Point Point => new(X, Y);
        internal bool IsValid => double.IsFinite(X) && double.IsFinite(Y);
    }
}

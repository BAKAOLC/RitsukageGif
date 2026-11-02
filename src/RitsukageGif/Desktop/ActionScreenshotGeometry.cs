namespace RitsukageGif.Desktop
{
    internal sealed record ActionScreenshotGeometry(int? X, int? Y, int? Width, int? Height, double? AspectRatio)
    {
        internal bool NeedsPointer => Width.HasValue && !X.HasValue;

        internal PixelRect? Resolve(PixelPoint pointer)
        {
            if (Width is not { } width || Height is not { } height) return null;
            return new(X ?? checked(pointer.X - width / 2), Y ?? checked(pointer.Y - height / 2), width, height);
        }
    }
}

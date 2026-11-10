namespace RitsukageGif.Contracts
{
    public sealed record PinInfo(
        string Id,
        string Kind,
        string Group,
        string Title,
        int X,
        int Y,
        int Width,
        int Height,
        double Zoom,
        double Opacity,
        bool Visible,
        bool Topmost,
        bool Locked,
        bool MouseThrough,
        bool CanMouseThrough,
        bool Thumbnail,
        bool Shadow,
        bool AnnotationsVisible,
        bool? Selectable,
        bool? IgnoreFormat,
        double ScrollX,
        double ScrollY,
        PinTransparentLayer? TransparentLayer,
        bool CanIgnoreTransparentInput);
}

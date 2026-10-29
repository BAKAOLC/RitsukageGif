namespace RitsukageGif.Desktop
{
    internal sealed record WindowControlTarget(
        // ReSharper disable once NotAccessedPositionalProperty.Global
        WindowTarget Window,
        nint Handle,
        PixelRect Bounds);
}

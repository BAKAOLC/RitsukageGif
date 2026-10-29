namespace RitsukageGif.Contracts
{
    // Pixels remain valid after the next capture. Rows are top-down; FP16 components are little-endian RGBA.
    public sealed record CapturedFrame(
        int Width,
        int Height,
        int Stride,
        CapturePixelFormat PixelFormat,
        ReadOnlyMemory<byte> Pixels,
        FrameColorInfo ColorInfo,
        CaptureAlphaMode AlphaMode,
        long Revision,
        IReadOnlyList<FrameColorRegion> ColorRegions);
}

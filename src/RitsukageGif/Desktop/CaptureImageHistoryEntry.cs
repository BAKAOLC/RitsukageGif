namespace RitsukageGif.Desktop
{
    internal sealed record CaptureImageHistoryEntry(
        ReadOnlyMemory<byte> Png,
        // ReSharper disable once NotAccessedPositionalProperty.Global
        PixelRect Region,
        // ReSharper disable once NotAccessedPositionalProperty.Global
        CaptureSourceKind Source,
        // ReSharper disable once NotAccessedPositionalProperty.Global
        DateTimeOffset Timestamp,
        CapturedFrame? Hdr)
    {
        internal double CaptureScaling { get; init; } = 1;
        internal CaptureDecoration Decoration { get; init; } = CaptureDecoration.None;
        internal PixelPoint? OutputOrigin { get; init; }
    }
}

namespace RitsukageGif.Contracts
{
    // scRGB uses Rec.709 primaries, D65 and linear light; 1.0 is always 80 cd/m².
    // SdrWhiteNits describes the source display's SDR reference; it never rescales stored HDR values.
    public sealed record FrameColorInfo(bool IsHdrSource, float SdrWhiteNits = 80)
    {
        public const float ScRgbReferenceWhiteNits = 80;
    }
}

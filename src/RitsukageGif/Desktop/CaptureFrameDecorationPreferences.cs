namespace RitsukageGif.Desktop
{
    public sealed record CaptureFrameDecorationPreferences
    {
        public CaptureDecorationKind Kind { get; set; } = CaptureDecorationKind.Shadow;
        public int ShadowStrength { get; set; } = 8;
        public uint ShadowColor { get; set; } = 0xFFA0A0A4;
        public int BorderStrength { get; set; } = 8;
        public uint BorderColor { get; set; } = 0xFFFFFFFF;

        public CaptureFrameDecorationPreferences Normalize()
        {
            return this with
            {
                Kind = Kind is CaptureDecorationKind.Shadow or CaptureDecorationKind.Border
                    ? Kind
                    : CaptureDecorationKind.Shadow,
                ShadowStrength = Math.Clamp(ShadowStrength, 1, 50),
                BorderStrength = Math.Clamp(BorderStrength, 1, 50),
            };
        }
    }
}

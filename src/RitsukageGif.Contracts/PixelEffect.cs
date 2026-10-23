namespace RitsukageGif.Contracts
{
    public sealed record PixelEffect(
        PixelEffectKind Kind,
        PixelRect? Region = null,
        double Amount = 1,
        int BlockSize = 8,
        int Radius = 4,
        uint Color = 0xff000000,
        double Brightness = 0,
        double Contrast = 1,
        PixelEffectSpace Space = PixelEffectSpace.Source)
    {
        public void Validate()
        {
            if (!Enum.IsDefined(Kind) || !Enum.IsDefined(Space) ||
                !double.IsFinite(Amount) || Amount is < 0 or > 1 ||
                BlockSize is < 1 or > 4096 || Radius is < 1 or > 256 ||
                !double.IsFinite(Brightness) || Brightness is < -1 or > 1 ||
                !double.IsFinite(Contrast) || Contrast is < 0 or > 8 || Region is { IsEmpty: true })
                throw new ArgumentException(UiText.Get("InvalidRecording"));
        }
    }
}

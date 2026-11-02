namespace RitsukageGif.Desktop
{
    internal sealed class NativeCaptureColor
    {
        public bool Hdr { get; set; }
        public string Primaries { get; set; } = "bt709";
        public string Transfer { get; set; } = "srgb";
        public float SdrWhiteNits { get; set; } = 80;
        public float LinearReferenceNits { get; set; } = 80;
        public float PeakNits { get; set; } = 1000;
    }
}

namespace RitsukageGif.Core
{
    public sealed record RecordingOptions(
        PixelRect Region,
        int Fps = 20,
        int Divisor = 2,
        bool Cursor = false,
        OutputFormat Format = OutputFormat.Gif,
        GifQuality Quality = GifQuality.Balanced,
        bool UseFfmpeg = false,
        bool Transparent = false)
    {
        public int Width => Math.Max(1, Region.Width / Divisor);
        public int Height => Math.Max(1, Region.Height / Divisor);

        public void Validate()
        {
            if (Region.IsEmpty || Fps is < 1 or > 100 || Divisor is < 1 or > 100)
                throw new ArgumentException(UiText.Get("InvalidRecording"));
            if (Width > 65535 || Height > 65535 || (long)Width * Height > 64 * 1024 * 1024)
                throw new ArgumentException(UiText.Get("RecordingTooLarge"));
        }
    }
}

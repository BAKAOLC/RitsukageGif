namespace RitsukageGif.Desktop
{
    internal sealed record RecordingCursorSample(
        ulong ShapeId,
        bool Visible,
        double X,
        double Y,
        int Width,
        int Height,
        int HotspotX,
        int HotspotY,
        double PixelScale,
        byte[] Pixels,
        byte[]? AndMask = null,
        int Buttons = 0)
    {
        internal bool HasVisiblePixels { get; } = ContainsInk(Pixels, AndMask);

        internal static RecordingCursorSample Hidden { get; } = new(0, false, 0, 0, 0, 0, 0, 0, 1, []);

        private static bool ContainsInk(byte[] pixels, byte[]? mask)
        {
            for (var offset = 0; offset < pixels.Length; offset += 4)
                if (mask is null)
                {
                    if (pixels[offset + 3] != 0) return true;
                }
                else if (mask[offset] != 255 || mask[offset + 1] != 255 || mask[offset + 2] != 255 ||
                         pixels[offset] != 0 || pixels[offset + 1] != 0 || pixels[offset + 2] != 0)
                {
                    return true;
                }

            return false;
        }
    }
}

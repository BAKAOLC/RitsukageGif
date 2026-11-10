namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class FrozenScreenshotCursor
    {
        internal (byte[] Pixels, byte[]? AndMask) ReadRecordingPixels()
        {
            if (ReadTransparentPixels() is { } transparent) return (transparent, null);
            var black = new byte[checked(_bounds.Width * _bounds.Height * 4)];
            var white = new byte[black.Length];
            Array.Fill(white, (byte)255);
            Draw(black, _bounds);
            Draw(white, _bounds);
            var mask = new byte[black.Length];
            for (var offset = 0; offset < black.Length; offset += 4)
            {
                for (var channel = 0; channel < 3; channel++)
                    mask[offset + channel] = (byte)(white[offset + channel] ^ black[offset + channel]);
                black[offset + 3] = 255;
            }

            return (black, mask);
        }
    }
}

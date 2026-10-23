using System.Buffers.Binary;

namespace RitsukageGif.Core
{
    public static class BitmapSpool
    {
        // Top-down BGRA avoids PNG compression and a second duration file on the capture path.
        public static async Task WriteAsync(string path, byte[] pixels, int width, int height, CancellationToken token)
        {
            if (pixels.Length != checked(width * height * 4))
                throw new ArgumentException(UiText.Get("InvalidFrameBuffer"));
            var header = new byte[122]; // BITMAPV4HEADER declares alpha explicitly for APNG/WebP/FFmpeg.
            header[0] = (byte)'B';
            header[1] = (byte)'M';
            Put(header, 2, 122 + pixels.Length);
            Put(header, 10, 122);
            Put(header, 14, 108);
            Put(header, 18, width);
            Put(header, 22, -height);
            header[26] = 1;
            header[28] = 32;
            Put(header, 30, 3);
            Put(header, 34, pixels.Length);
            Put(header, 54, 0x00ff0000);
            Put(header, 58, 0x0000ff00);
            Put(header, 62, 0x000000ff);
            Put(header, 66, unchecked((int)0xff000000));
            Put(header, 70, 0x73524742);
            await using var stream =
                new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 65536, true);
            await stream.WriteAsync(header, token);
            await stream.WriteAsync(pixels, token);
        }

        private static void Put(byte[] data, int at, int value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(at), value);
        }
    }
}

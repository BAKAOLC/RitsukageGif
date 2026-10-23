using System.Buffers.Binary;

namespace RitsukageGif.Desktop
{
    internal readonly record struct CaptureFrameHeader(int Width, int Height, int OriginX, int OriginY, int Length)
    {
        public static CaptureFrameHeader Parse(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length != 24 || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != 0x46434752)
                throw Invalid();
            var width = BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]);
            var height = BinaryPrimitives.ReadInt32LittleEndian(bytes[8..]);
            var x = BinaryPrimitives.ReadInt32LittleEndian(bytes[12..]);
            var y = BinaryPrimitives.ReadInt32LittleEndian(bytes[16..]);
            var count = BinaryPrimitives.ReadUInt32LittleEndian(bytes[20..]);
            if (width <= 0 || height <= 0 || width > 32768 || height > 32768 ||
                (long)width * height * 4 != count || count > 512 * 1024 * 1024 ||
                (long)x + width > int.MaxValue || (long)y + height > int.MaxValue)
                throw Invalid();
            return new CaptureFrameHeader(width, height, x, y, (int)count);
        }

        private static InvalidDataException Invalid() => new(UiText.Get("InvalidFrameBuffer"));
    }
}

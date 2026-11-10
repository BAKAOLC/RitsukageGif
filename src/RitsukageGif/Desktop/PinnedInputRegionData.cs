using System.Buffers.Binary;

namespace RitsukageGif.Desktop
{
    internal static class PinnedInputRegionData
    {
        internal static byte[] Pack(PinnedInputRegion region)
        {
            var rectangles = region.Rectangles;
            var bytes = new byte[checked(32 + rectangles.Length * 16)];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, 32);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), 1);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), rectangles.Length);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), rectangles.Length * 16);
            var left = 0;
            var top = 0;
            var right = 0;
            var bottom = 0;
            for (var index = 0; index < rectangles.Length; index++)
            {
                var rectangle = rectangles[index];
                if (index == 0)
                {
                    left = rectangle.X;
                    top = rectangle.Y;
                }
                else
                {
                    left = Math.Min(left, rectangle.X);
                    top = Math.Min(top, rectangle.Y);
                }

                right = Math.Max(right, rectangle.Right);
                bottom = Math.Max(bottom, rectangle.Bottom);
                var output = bytes.AsSpan(32 + index * 16, 16);
                BinaryPrimitives.WriteInt32LittleEndian(output, rectangle.X);
                BinaryPrimitives.WriteInt32LittleEndian(output[4..], rectangle.Y);
                BinaryPrimitives.WriteInt32LittleEndian(output[8..], rectangle.Right);
                BinaryPrimitives.WriteInt32LittleEndian(output[12..], rectangle.Bottom);
            }

            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), left);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(20), top);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(24), right);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(28), bottom);
            return bytes;
        }
    }
}

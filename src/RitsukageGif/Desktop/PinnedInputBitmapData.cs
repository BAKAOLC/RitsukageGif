namespace RitsukageGif.Desktop
{
    internal static class PinnedInputBitmapData
    {
        internal static byte[] Pack(PinnedInputRegion coverage)
        {
            var stride = checked((coverage.Size.Width + 7) / 8);
            var output = new byte[checked(stride * coverage.Size.Height)];
            foreach (var rectangle in coverage.Rectangles)
                for (var y = rectangle.Y; y < rectangle.Bottom; y++)
                {
                    var row = output.AsSpan(y * stride, stride);
                    var first = rectangle.X / 8;
                    var last = (rectangle.Right - 1) / 8;
                    var left = (byte)(0xff << (rectangle.X & 7));
                    var right = (byte)(0xff >> (7 - ((rectangle.Right - 1) & 7)));
                    if (first == last)
                    {
                        row[first] |= (byte)(left & right);
                    }
                    else
                    {
                        row[first] |= left;
                        if (last > first + 1) row[(first + 1)..last].Fill(0xff);
                        row[last] |= right;
                    }
                }

            return output;
        }
    }
}

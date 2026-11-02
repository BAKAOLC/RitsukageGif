namespace RitsukageGif.Core
{
    internal static class GrayInkLayout
    {
        internal static GrayImage[] Lines(GrayImage image, TableStructureCell cell)
        {
            var left = Math.Clamp((int)Math.Floor(cell.X), 0, image.Width - 1);
            var top = Math.Clamp((int)Math.Floor(cell.Y), 0, image.Height - 1);
            var width = Math.Clamp((int)Math.Ceiling(cell.Width), 1, image.Width - left);
            var height = Math.Clamp((int)Math.Ceiling(cell.Height), 1, image.Height - top);
            var pixels = new byte[checked(width * height)];
            var histogram = new int[256];
            for (var y = 0; y < height; y++)
            {
                image.Pixels.AsSpan((top + y) * image.Width + left, width).CopyTo(pixels.AsSpan(y * width));
                for (var x = 0; x < width; x++) histogram[pixels[y * width + x]]++;
            }

            var minimum = Array.FindIndex(histogram, count => count != 0);
            var maximum = Array.FindLastIndex(histogram, count => count != 0);
            if (minimum == maximum) return [];
            var background = 0;
            for (var index = 1; index < histogram.Length; index++)
                if (histogram[index] > histogram[background])
                    background = index;
            for (var index = 0; index < pixels.Length; index++)
            {
                var value = (pixels[index] - minimum) * 255 / (maximum - minimum);
                pixels[index] = (byte)(background < 128 ? 255 - value : value);
            }

            var rows = new List<(int Start, int End)>();
            var start = -1;
            for (var y = 0; y <= height; y++)
            {
                var count = 0;
                if (y < height)
                    for (var x = 0; x < width; x++)
                        if (pixels[y * width + x] < 160)
                            count++;
                if (count > 1)
                {
                    if (start < 0) start = y;
                }
                else if (start >= 0)
                {
                    rows.Add((start, y));
                    start = -1;
                }
            }

            if (rows.Count == 0) return [];
            var largest = rows.Max(row => row.End - row.Start);
            for (var index = 1; index < rows.Count;)
            {
                var previous = rows[index - 1];
                var current = rows[index];
                var gap = current.Start - previous.End;
                if (gap <= 2 || (gap < largest / 2 &&
                                 Math.Min(previous.End - previous.Start, current.End - current.Start) < largest / 2))
                {
                    rows[index - 1] = (previous.Start, current.End);
                    rows.RemoveAt(index);
                }
                else
                {
                    index++;
                }
            }

            var lines = new List<GrayImage>();
            foreach (var row in rows)
            {
                var x0 = width;
                var x1 = -1;
                for (var y = row.Start; y < row.End; y++)
                for (var x = 0; x < width; x++)
                    if (pixels[y * width + x] < 160)
                    {
                        x0 = Math.Min(x0, x);
                        x1 = Math.Max(x1, x);
                    }

                var lineWidth = x1 - x0 + 5;
                var lineHeight = row.End - row.Start + 4;
                var linePixels = new byte[checked(lineWidth * lineHeight)];
                Array.Fill(linePixels, (byte)255);
                for (var y = row.Start; y < row.End; y++)
                    pixels.AsSpan(y * width + x0, x1 - x0 + 1)
                        .CopyTo(linePixels.AsSpan((y - row.Start + 2) * lineWidth + 2));
                lines.Add(new(linePixels, lineWidth, lineHeight));
            }

            return [.. lines];
        }
    }
}

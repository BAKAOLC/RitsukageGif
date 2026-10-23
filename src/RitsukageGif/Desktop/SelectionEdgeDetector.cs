namespace RitsukageGif.Desktop
{
    /// <summary>Finds coherent axis-aligned boundaries in a bounded area of the frozen screen image.</summary>
    internal sealed class SelectionEdgeDetector
    {
        private readonly int _width;
        private readonly int _height;
        private readonly byte[] _pixels;

        public SelectionEdgeDetector(int width, int height, byte[] pixels)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
            ArgumentNullException.ThrowIfNull(pixels);
            if ((long)width * height * 4 != pixels.LongLength)
                throw new ArgumentException("The image must contain tightly packed BGRA pixels.", nameof(pixels));
            _width = width;
            _height = height;
            _pixels = pixels;
        }

        public int Find(int primary, int secondary, bool vertical, int radius, int threshold, double scale)
        {
            var length = vertical ? _width : _height;
            var cross = vertical ? _height : _width;
            if (radius <= 0 || primary < 0 || primary >= length || secondary < 0 || secondary >= cross)
                return primary;

            // Both the search radius and supporting line length are logical pixels, independently per monitor.
            scale = double.IsFinite(scale) ? Math.Clamp(scale, 0.5, 8) : 1;
            var reach = (int)Math.Ceiling(Math.Clamp(radius, 0, 50) * scale);
            var halfSpan = (int)Math.Ceiling(32 * scale);
            var first = Math.Max(0, secondary - halfSpan);
            var last = Math.Min(cross - 1, secondary + halfSpan);
            var count = last - first + 1;
            if (count < Math.Ceiling(8 * scale)) return primary;

            threshold = Math.Clamp(threshold, 1, 765);
            var weak = Math.Max(6, threshold / 6);
            var strong = Math.Max(12, threshold / 3);
            var minimumRun = Math.Max((int)Math.Ceiling(18 * scale), (count * 3 + 9) / 10);
            var minimumSupport = (count * 3 + 9) / 10;
            var best = primary;
            var bestStrength = 0;
            var bestDistance = int.MaxValue;
            for (var p = Math.Max(1, primary - reach); p <= Math.Min(length - 1, primary + reach); p++)
            {
                var support = 0;
                var run = 0;
                var longest = 0;
                var magnitude = 0;
                var blue = 0;
                var green = 0;
                var red = 0;
                var nearPointer = false;
                for (var q = first; q <= last; q++)
                {
                    // A boundary at p separates pixels p-1 and p. Unlike a centred difference this
                    // cannot report the edge one pixel outside the actual selection boundary.
                    var a = vertical ? (q * _width + p - 1) * 4 : ((p - 1) * _width + q) * 4;
                    var b = vertical ? a + 4 : a + _width * 4;
                    var db = _pixels[b] - _pixels[a];
                    var dg = _pixels[b + 1] - _pixels[a + 1];
                    var dr = _pixels[b + 2] - _pixels[a + 2];
                    var strength = Math.Abs(db) + Math.Abs(dg) + Math.Abs(dr);
                    if (strength < weak)
                    {
                        run = 0;
                        continue;
                    }

                    support++;
                    longest = Math.Max(longest, ++run);
                    magnitude += strength;
                    blue += db;
                    green += dg;
                    red += dr;
                    nearPointer |= Math.Abs(q - secondary) <= reach;
                }

                // Short glyph strokes and alternating texture gradients are not stable region edges.
                // One-sided support accepts a nearby rectangle corner, including clipped monitor edges.
                if (!nearPointer || support < minimumSupport || longest < minimumRun ||
                    magnitude < support * strong) continue;
                var coherent = Math.Abs(blue) + Math.Abs(green) + Math.Abs(red);
                if (coherent * 5 < magnitude * 4) continue;
                var distance = Math.Abs(p - primary);
                if (distance > bestDistance || (distance == bestDistance && coherent <= bestStrength)) continue;
                bestDistance = distance;
                bestStrength = coherent;
                best = p;
            }

            return best;
        }
    }
}

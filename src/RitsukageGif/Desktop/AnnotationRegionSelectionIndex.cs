using Avalonia;

namespace RitsukageGif.Desktop
{
    internal sealed class AnnotationRegionSelectionIndex
    {
        private const int Side = 32;
        private const double Coverage = .92;
        private readonly List<Rect>?[] _buckets = new List<Rect>?[Side * Side];

        public AnnotationRegionSelectionIndex(IEnumerable<Rect> regions)
        {
            foreach (var region in regions)
            {
                if (region.Width <= 0 || region.Height <= 0) continue;
                var index = Bucket(region.Center.X) + Bucket(region.Center.Y) * Side;
                (_buckets[index] ??= []).Add(region);
            }
        }

        public bool ContainsEquivalent(Rect candidate)
        {
            if (candidate.Width <= 0 || candidate.Height <= 0) return false;
            var center = candidate.Center;
            var left = Math.Max(0, Bucket(center.X - candidate.Width * (1 - Coverage)) - 1);
            var right = Math.Min(Side - 1, Bucket(center.X + candidate.Width * (1 - Coverage)) + 1);
            var top = Math.Max(0, Bucket(center.Y - candidate.Height * (1 - Coverage)) - 1);
            var bottom = Math.Min(Side - 1, Bucket(center.Y + candidate.Height * (1 - Coverage)) + 1);
            var required = candidate.Width * candidate.Height * Coverage;
            for (var y = top; y <= bottom; y++)
            for (var x = left; x <= right; x++)
            {
                if (_buckets[y * Side + x] is not { } regions) continue;
                // ReSharper disable once ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator
                foreach (var region in regions)
                {
                    var intersection = candidate.Intersect(region);
                    var area = intersection.Width * intersection.Height;
                    if (area >= required && area >= region.Width * region.Height * Coverage) return true;
                }
            }

            return false;
        }

        private static int Bucket(double coordinate)
        {
            return Math.Clamp((int)Math.Floor(coordinate * Side), 0, Side - 1);
        }
    }
}

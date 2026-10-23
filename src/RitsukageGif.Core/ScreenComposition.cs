namespace RitsukageGif.Core
{
    public static class ScreenComposition
    {
        public static CaptureSegment[] Split(PixelRect selection, IEnumerable<PixelRect> monitors)
        {
            return monitors
                .Select(selection.Intersect)
                .Where(region => !region.IsEmpty)
                .Distinct()
                .Select(region => new CaptureSegment(region, region.X - selection.X, region.Y - selection.Y))
                .ToArray();
        }
    }
}

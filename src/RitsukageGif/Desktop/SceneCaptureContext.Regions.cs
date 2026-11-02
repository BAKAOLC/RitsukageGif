namespace RitsukageGif.Desktop
{
    internal sealed partial class SceneCaptureContext
    {
        private static bool PrepareWindowRegion(ISceneWindowSource capture, WindowRegionRequest request,
            PixelRect region, int outputWidth, int outputHeight, bool hasFrame)
        {
            if (capture is not ISceneRegionSource { RegionCaptureSupported: true } source) return !hasFrame;
            var density = Math.Max((double)outputWidth / region.Width, (double)outputHeight / region.Height);
            request.Current = request.Current is { } current ? Union(current, region) : region;
            request.CurrentDensity = Math.Max(request.CurrentDensity, density);
            var required = request.Previous is { } previous
                ? Union(previous, request.Current.Value)
                : request.Current.Value;
            var requiredDensity = Math.Max(request.PreviousDensity, request.CurrentDensity);
            if (hasFrame && request.Configured is { } configured && required.Intersect(configured) == required &&
                request.ConfiguredDensity >= requiredDensity) return false;
            source.ConfigureRegion(required, checked((int)Math.Ceiling(required.Width * requiredDensity)),
                checked((int)Math.Ceiling(required.Height * requiredDensity)));
            request.Configured = required;
            request.ConfiguredDensity = requiredDensity;
            return true;
        }

        private static PixelRect Union(PixelRect left, PixelRect right)
        {
            var x = Math.Min(left.X, right.X);
            var y = Math.Min(left.Y, right.Y);
            return new(x, y, checked(Math.Max(left.Right, right.Right) - x),
                checked(Math.Max(left.Bottom, right.Bottom) - y));
        }

        private sealed class WindowRegionRequest
        {
            internal PixelRect? Current, Previous, Configured;
            internal double CurrentDensity, PreviousDensity, ConfiguredDensity;

            internal void BeginFrame()
            {
                Previous = Current;
                PreviousDensity = CurrentDensity;
                Current = null;
                CurrentDensity = 0;
            }
        }
    }
}

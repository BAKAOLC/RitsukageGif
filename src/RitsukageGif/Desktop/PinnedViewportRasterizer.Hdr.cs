using Avalonia;

namespace RitsukageGif.Desktop
{
    internal static partial class PinnedViewportRasterizer
    {
        internal static CapturedFrame Rasterize(CapturedFrame source, Matrix sourceToTarget, int targetWidth,
            int targetHeight, double opacity, bool smoothScaling, PinnedViewportClip? clip = null,
            CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (source.PixelFormat != CapturePixelFormat.Rgba16FloatScRgb)
                throw new ArgumentException("The HDR viewport source must be RGBA FP16 scRGB.", nameof(source));
            var output = Allocate(targetWidth, targetHeight, 8);
            RenderProjection(new PinnedViewportFrameSource(source), new PinnedViewportFrameTarget(output),
                sourceToTarget,
                targetWidth, targetHeight, opacity, smoothScaling, clip, token);
            var regions = new List<FrameColorRegion>(source.ColorRegions.Count);
            foreach (var color in source.ColorRegions)
            {
                token.ThrowIfCancellationRequested();
                var clipped = color.Region.Intersect(new(0, 0, source.Width, source.Height));
                if (clipped.IsEmpty) continue;
                var bounds =
                    new Rect(clipped.X, clipped.Y, clipped.Width, clipped.Height).TransformToAABB(sourceToTarget);
                var left = (int)Math.Clamp(Math.Floor(bounds.Left), 0, targetWidth);
                var top = (int)Math.Clamp(Math.Floor(bounds.Top), 0, targetHeight);
                var right = (int)Math.Clamp(Math.Ceiling(bounds.Right), 0, targetWidth);
                var bottom = (int)Math.Clamp(Math.Ceiling(bounds.Bottom), 0, targetHeight);
                var region = new PixelRect(left, top, right - left, bottom - top);
                if (!region.IsEmpty) regions.Add(color with { Region = region });
            }

            return source with
            {
                Width = targetWidth, Height = targetHeight, Stride = checked(targetWidth * 8), Pixels = output,
                AlphaMode = CaptureAlphaMode.Premultiplied, ColorRegions = [.. regions],
            };
        }
    }
}

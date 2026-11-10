using Avalonia;

namespace RitsukageGif.Desktop
{
    internal static class PinnedViewportOutput
    {
        internal static async Task<ScreenshotResult> ProjectAsync(Task<ScreenshotResult> pending,
            PinnedViewportSnapshot snapshot, CancellationToken token)
        {
            var source = await pending;
            token.ThrowIfCancellationRequested();
            if (snapshot is { UseView: false, Opacity: 1 }) return source;
            return await Task.Run(() =>
            {
                var dimensions = PngImageCodec.ReadDimensions(source.Png);
                var size = snapshot.UseView ? snapshot.TargetSize : new(dimensions.Width, dimensions.Height);
                var matrix = snapshot.UseView ? snapshot.SourceToTarget : Matrix.Identity;
                var clip = snapshot.UseView ? snapshot.Clip : null;
                return source with
                {
                    Png = PinnedViewportRasterizer.RenderPng(source.Png, matrix, size.Width, size.Height,
                        snapshot.Opacity, snapshot.SmoothScaling, clip, token),
                    Hdr = source.Hdr is { } hdr
                        ? PinnedViewportRasterizer.Rasterize(hdr, matrix, size.Width, size.Height,
                            snapshot.Opacity, snapshot.SmoothScaling, clip, token)
                        : null,
                    CaptureScaling = snapshot.UseView ? snapshot.RenderScaling : source.CaptureScaling,
                };
            }, token);
        }
    }
}

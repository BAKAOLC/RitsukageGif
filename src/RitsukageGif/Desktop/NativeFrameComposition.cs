namespace RitsukageGif.Desktop
{
    internal static class NativeFrameComposition
    {
        internal static CapturedFrameSet Compose(IReadOnlyList<(PixelRect Bounds, CapturedFrameSet Frames)> sources,
            PixelRect region, double density, CancellationToken token)
        {
            var options = new RecordingOptions(region, Divisor: 1) { PixelDensity = density };
            options.Validate();
            if (sources is [{ Bounds: var bounds, Frames: var frames }] && bounds == region &&
                frames.Sdr.Width == options.Width && frames.Sdr.Height == options.Height)
                return frames;
            using var composer = new PortableFrameComposer(options.Width, options.Height,
                sources.Any(source => source.Frames.Hdr is not null));
            composer.Begin();
            for (var i = 0; i < sources.Count; i++)
            {
                var source = sources[i];
                composer.Draw(new(source.Frames, new(), null, null), source.Bounds, region, i, token: token);
            }

            return composer.Finish(token);
        }
    }
}

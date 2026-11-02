namespace RitsukageGif.Desktop
{
    internal sealed partial class SelectionCoordinator
    {
        private FrozenCaptureResult CaptureSelectedWindows(WindowTarget[] targets,
            PixelRect region, CancellationToken token)
        {
            switch (targets.Length)
            {
                case 0:
                    throw new CaptureSourceLostException();
                case 1:
                    return CaptureWindowPixels(new(region, true, targets[0]), token);
            }

            var pixels = new byte[checked(region.Width * region.Height * 4)];
            var hdrFrames = new List<(PixelRect Region, CapturedFrame? Frame)>();
            var nativeFrames = new List<(PixelRect Bounds, CapturedFrameSet Frames)>();
            foreach (var target in targets.Reverse())
            {
                var source = CaptureWindowPixels(new(target.Client, true, target), token);
                var intersection = region.Intersect(target.Client);
                for (var row = 0; row < intersection.Height; row++)
                {
                    token.ThrowIfCancellationRequested();
                    var offset = ((intersection.Y - target.Client.Y + row) * target.Client.Width +
                        intersection.X - target.Client.X) * 4;
                    var destination = ((intersection.Y - region.Y + row) * region.Width +
                        intersection.X - region.X) * 4;
                    source.Pixels.AsSpan(offset, intersection.Width * 4)
                        .CopyTo(pixels.AsSpan(destination, intersection.Width * 4));
                }

                hdrFrames.Add((target.Client, source.Hdr));
                nativeFrames.Add((target.Client, source.NativeFrames ?? CreateSdrFrames(target.Client.Width,
                        target.Client.Height, source.Pixels) with
                    {
                        Hdr = source.Hdr,
                    }));
            }

            var density = OperatingSystem.IsMacOS() ? NativeDisplayCatalog.Density(region) : 1;
            return new(pixels, MergeHdrScreenshot(region, hdrFrames, token, false).Frame,
                density > 1 ? NativeFrameComposition.Compose(nativeFrames, region, density, token) : null);
        }
    }
}

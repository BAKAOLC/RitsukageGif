using System.Diagnostics;

namespace RitsukageGif.Desktop
{
    internal sealed class LongCaptureFrameSource(
        ScreenshotScrollingSource capture,
        NativeCaptureSessionReference? nativeSource) : ILongCaptureFrameSource
    {
        public Task<CapturedFrameSet> CaptureAsync(PixelRect region, bool retainHdr,
            CancellationToken token = default)
        {
            return Task.Run(() =>
            {
                if (retainHdr)
                {
                    var frames = capture.CaptureFrames(region, token);
                    var hdr = frames.Hdr ?? throw new NotSupportedException(UiText.Get("HdrCaptureUnavailable"));
                    if (!(hdr.ColorRegions.Count == 0
                            ? hdr.ColorInfo.IsHdrSource
                            : hdr.ColorRegions.Any(part => part.ColorInfo.IsHdrSource)))
                        throw new NotSupportedException(UiText.Get("HdrCaptureUnavailable"));
                    return frames;
                }

                var pixels = capture.Capture(region, token);
                var color = new FrameColorInfo(false);
                return new(new(region.Width, region.Height, checked(region.Width * 4),
                    CapturePixelFormat.Bgra8Srgb, pixels, color, CaptureAlphaMode.Straight, 0,
                    [new(new(0, 0, region.Width, region.Height), color)]), null, Stopwatch.GetTimestamp());
            }, token);
        }

        public ValueTask SuspendAsync()
        {
            capture.Suspend();
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            try
            {
                capture.Dispose();
            }
            finally
            {
                nativeSource?.Dispose();
            }

            return ValueTask.CompletedTask;
        }
    }
}

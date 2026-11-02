namespace RitsukageGif.Core
{
    public static partial class ImageStripService
    {
        private static CapturedFrame Delete(CapturedFrame frame, ImageStripAxis axis, int start, int count,
            CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(frame);
            token.ThrowIfCancellationRequested();
            ValidateBand(frame.Width, frame.Height, axis, start, count);
            var bytesPerPixel = frame.PixelFormat switch
            {
                CapturePixelFormat.Bgra8Srgb => 4,
                CapturePixelFormat.Rgba16FloatScRgb => 8,
                _ => throw new ArgumentException(UiText.Get("InvalidFrameBuffer"), nameof(frame)),
            };
            if (frame.Width <= 0 || frame.Height <= 0 || frame.Stride < (long)frame.Width * bytesPerPixel ||
                frame.Pixels.Length < (long)frame.Stride * frame.Height)
                throw new ArgumentException(UiText.Get("InvalidFrameBuffer"), nameof(frame));
            var width = frame.Width - (axis == ImageStripAxis.Columns ? count : 0);
            var height = frame.Height - (axis == ImageStripAxis.Rows ? count : 0);
            var pixels = RemoveSamples(frame.Pixels.Span, frame.Width, frame.Height, frame.Stride,
                bytesPerPixel, axis, start, count, token);
            return frame with
            {
                Width = width, Height = height, Stride = checked(width * bytesPerPixel), Pixels = pixels,
                ColorRegions = MoveColorRegions(frame, axis, start, count),
            };
        }

        private static FrameColorRegion[] MoveColorRegions(CapturedFrame frame, ImageStripAxis axis, int start,
            int count)
        {
            var first = axis == ImageStripAxis.Rows
                ? new PixelRect(0, 0, frame.Width, start)
                : new PixelRect(0, 0, start, frame.Height);
            var second = axis == ImageStripAxis.Rows
                ? new PixelRect(0, start + count, frame.Width, frame.Height - start - count)
                : new PixelRect(start + count, 0, frame.Width - start - count, frame.Height);
            var regions = new List<FrameColorRegion>(frame.ColorRegions.Count * 2);
            foreach (var color in frame.ColorRegions)
            {
                var before = color.Region.Intersect(first);
                if (!before.IsEmpty) regions.Add(color with { Region = before });
                var after = color.Region.Intersect(second);
                if (!after.IsEmpty)
                    regions.Add(color with
                    {
                        Region = after with
                        {
                            X = after.X - (axis == ImageStripAxis.Columns ? count : 0),
                            Y = after.Y - (axis == ImageStripAxis.Rows ? count : 0),
                        },
                    });
            }

            return [.. regions];
        }
    }
}

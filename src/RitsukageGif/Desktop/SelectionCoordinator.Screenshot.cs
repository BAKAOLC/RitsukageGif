using ImageMagick;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SelectionCoordinator
    {
        private Task<byte[]> ExportScreenshotAsync(SelectionResult selection, CancellationToken token = default)
        {
            var window = selection.WindowSource && !UsesLocalSource
                ? _frozenWindow?.Target.Handle == selection.Target?.Handle
                    ? _frozenWindow
                    : throw new CaptureSourceLostException()
                : null;
            var frames = window is null ? _screenshots.ToArray() : [(window.Region, window.Pixels)];
            return Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                new RecordingOptions(selection.Region, Divisor: 1).Validate();
                return EncodeFrozenScreenshot(selection.Region, frames,
                    selection.Cursor && !UsesLocalSource ? ScreenshotCursor : null, token);
            }, token);
        }

        private static byte[] EncodeFrozenScreenshot(PixelRect region,
            IReadOnlyList<(PixelRect Region, byte[] Pixels)> frames, FrozenScreenshotCursor? cursor = null,
            CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            new RecordingOptions(region, Divisor: 1).Validate();
            var pixels = new byte[checked(region.Width * region.Height * 4)];
            for (var alpha = 3; alpha < pixels.Length; alpha += 4) pixels[alpha] = 255;
            foreach (var frame in frames)
            {
                token.ThrowIfCancellationRequested();
                var intersection = region.Intersect(frame.Region);
                if (intersection.IsEmpty) continue;
                if (frame.Pixels.Length != checked(frame.Region.Width * frame.Region.Height * 4))
                    throw new ArgumentException(UiText.Get("InvalidRecording"));
                var rowBytes = checked(intersection.Width * 4);
                for (var y = 0; y < intersection.Height; y++)
                {
                    token.ThrowIfCancellationRequested();
                    var source = checked(((intersection.Y - frame.Region.Y + y) * frame.Region.Width +
                        intersection.X - frame.Region.X) * 4);
                    var destination = checked(((intersection.Y - region.Y + y) * region.Width +
                        intersection.X - region.X) * 4);
                    frame.Pixels.AsSpan(source, rowBytes).CopyTo(pixels.AsSpan(destination, rowBytes));
                }
            }

            token.ThrowIfCancellationRequested();
            cursor?.Draw(pixels, region);
            using var image = new MagickImage(pixels,
                new PixelReadSettings((uint)region.Width, (uint)region.Height, StorageType.Char, PixelMapping.BGRA));
            image.Progress += (_, progress) => progress.Cancel = token.IsCancellationRequested;
            try
            {
                var png = image.ToByteArray(MagickFormat.Png);
                token.ThrowIfCancellationRequested();
                return png;
            }
            catch (MagickException) when (token.IsCancellationRequested)
            {
                throw new OperationCanceledException(token);
            }
        }

        private (byte[] Pixels, CapturedFrame? Hdr) CaptureWindowPixels(SelectionResult selection,
            CancellationToken token, bool allowHdr = true)
        {
            var target = selection.Target ?? throw new CaptureSourceLostException();
            using var capture = new WindowContentCapture(target.Handle, selection.Cursor);
            // Native window capture waits for a real first frame rather than returning an uninitialized buffer.
            CapturedFrameSet? frames = null;
            if (_retainHdr && allowHdr)
                try
                {
                    frames = capture.CaptureFrames(token);
                }
                catch (Exception error) when (IsOptionalHdrUnsupported(error))
                {
                    token.ThrowIfCancellationRequested();
                    // ReSharper disable once DisposeOnUsingVariable
                    capture.Dispose();
                    return CaptureWindowPixels(selection, token, false);
                }

            var pixels = frames is null
                ? capture.Capture(token) ?? throw new CaptureSourceLostException()
                : PackSdrFrame(frames.Sdr);
            var bounds = OperatingSystem.IsLinux() ? target.Client : target.Bounds;
            if (bounds.IsEmpty || capture.Width <= 0 || capture.Height <= 0) throw new CaptureSourceLostException();
            var region = selection.Region.Intersect(bounds);
            if (region.IsEmpty) throw new CaptureSourceLostException();
            var hdr = CropWindowHdr(frames, bounds, region, token);
            if (hdr is not null)
                return (PackSdrFrame(HdrToneMapping.Map(hdr, _screenshotToneMap, token)), hdr);
            if (capture.Width == bounds.Width && capture.Height == bounds.Height)
            {
                if (region == bounds) return (pixels, hdr);
                var cropped = new byte[checked(region.Width * region.Height * 4)];
                for (var row = 0; row < region.Height; row++)
                {
                    token.ThrowIfCancellationRequested();
                    pixels.AsSpan(((region.Y - bounds.Y + row) * bounds.Width + region.X - bounds.X) * 4,
                            region.Width * 4)
                        .CopyTo(cropped.AsSpan(row * region.Width * 4, region.Width * 4));
                }

                return (cropped, hdr);
            }

            using var image = new MagickImage(pixels,
                new PixelReadSettings((uint)capture.Width, (uint)capture.Height, StorageType.Char, PixelMapping.BGRA));
            var x = (int)Math.Floor((double)(region.X - bounds.X) * capture.Width / bounds.Width);
            var y = (int)Math.Floor((double)(region.Y - bounds.Y) * capture.Height / bounds.Height);
            var right = (int)Math.Ceiling((double)(region.Right - bounds.X) * capture.Width / bounds.Width);
            var bottom = (int)Math.Ceiling((double)(region.Bottom - bounds.Y) * capture.Height / bounds.Height);
            image.Crop(new MagickGeometry(x, y, (uint)(right - x), (uint)(bottom - y)));
            image.ResetPage();
            if (image.Width != region.Width || image.Height != region.Height)
                image.Resize(new MagickGeometry((uint)region.Width, (uint)region.Height) { IgnoreAspectRatio = true });
            token.ThrowIfCancellationRequested();
            using var result = image.GetPixels();
            return (result.ToByteArray(PixelMapping.BGRA)!, hdr);
        }

        private static CapturedFrame? CropWindowHdr(CapturedFrameSet? frames, PixelRect bounds, PixelRect region,
            CancellationToken token)
        {
            if (frames?.Hdr is not { PixelFormat: CapturePixelFormat.Rgba16FloatScRgb } frame) return null;
            if (frame.Width != bounds.Width || frame.Height != bounds.Height)
            {
                var left = (int)Math.Floor((double)(region.X - bounds.X) * frame.Width / bounds.Width);
                var top = (int)Math.Floor((double)(region.Y - bounds.Y) * frame.Height / bounds.Height);
                var right = (int)Math.Ceiling((double)(region.Right - bounds.X) * frame.Width / bounds.Width);
                var bottom = (int)Math.Ceiling((double)(region.Bottom - bounds.Y) * frame.Height / bounds.Height);
                return ColorFrameComposition.Compose([(new(0, 0, frame.Width, frame.Height), frames)],
                    new(left, top, right - left, bottom - top), region.Width, region.Height, frame.Revision, token).Hdr;
            }

            var pixels = new byte[checked(region.Width * region.Height * 8)];
            for (var row = 0; row < region.Height; row++)
            {
                token.ThrowIfCancellationRequested();
                frame.Pixels.Span.Slice((region.Y - bounds.Y + row) * frame.Stride + (region.X - bounds.X) * 8,
                    region.Width * 8).CopyTo(pixels.AsSpan(row * region.Width * 8, region.Width * 8));
            }

            var colors = new List<FrameColorRegion>();
            foreach (var part in frame.ColorRegions)
            {
                var clipped = new PixelRect(bounds.X + part.Region.X, bounds.Y + part.Region.Y,
                    part.Region.Width, part.Region.Height).Intersect(region);
                if (!clipped.IsEmpty)
                    colors.Add(part with
                    {
                        Region = clipped with
                        {
                            X = clipped.X - region.X, Y = clipped.Y - region.Y,
                        },
                    });
            }

            return frame with
            {
                Width = region.Width, Height = region.Height, Stride = region.Width * 8,
                Pixels = pixels, ColorRegions = colors,
            };
        }
    }
}

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
                    selection.Cursor ? ScreenshotCursor : null, token);
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

        private FrozenCaptureResult CaptureWindowPixels(SelectionResult selection,
            CancellationToken token, bool allowHdr = true)
        {
            var target = selection.Target ?? throw new CaptureSourceLostException();
            ValidateScreenshotTarget(target);
            using var capture = new WindowContentCapture(target.Handle, false);
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

            if (frames is null)
            {
                var raw = capture.Capture(token) ?? throw new CaptureSourceLostException();
                frames = CreateSdrFrames(capture.Width, capture.Height, raw);
            }

            var bounds = OperatingSystem.IsLinux() ? target.Client : target.Bounds;
            ValidateScreenshotTarget(target);
            var region = selection.Region.Intersect(bounds);
            if (region.IsEmpty) throw new CaptureSourceLostException();
            var sources = new[] { (bounds, frames) };
            var logical = NativeFrameComposition.Compose(sources, region, 1, token);
            var density = OperatingSystem.IsMacOS()
                ? Math.Max((double)frames.Sdr.Width / bounds.Width,
                    (double)frames.Sdr.Height / bounds.Height)
                : 1;
            var native = density > 1 ? NativeFrameComposition.Compose(sources, region, density, token) : null;
            return new(PackScreenshotFrame(logical, token), logical.Hdr, native);
        }

        private static void ValidateScreenshotTarget(WindowTarget target)
        {
            var refreshed = OperatingSystem.IsWindows()
                ? WindowLocator.RefreshCandidate(target)
                : WindowLocator.Refresh(target);
            if (refreshed is null || !IsSameSelectionWindow(target, refreshed) ||
                refreshed.Client != target.Client || (!OperatingSystem.IsLinux() && refreshed.Bounds != target.Bounds))
                throw new CaptureSourceLostException();
        }
    }
}

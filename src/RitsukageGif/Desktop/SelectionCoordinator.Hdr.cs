using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SelectionCoordinator
    {
        private readonly List<(PixelRect Region, CapturedFrame? Frame)> _hdrScreenshots = [];
        private readonly bool _retainHdr;
        private readonly HdrToneMapMode _screenshotToneMap;

        internal CapturedFrame? HdrScreenshotFor(PixelRect region)
        {
            // ReSharper disable once ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator
            foreach (var item in _hdrScreenshots)
                if (item.Region == region)
                    return item.Frame;
            return null;
        }

        internal static bool IsOptionalHdrUnsupported(Exception error)
        {
            return error is NotSupportedException || (error is COMException &&
                                                      error.HResult is unchecked((int)0x80070057)
                                                          or unchecked((int)0x80004001) or unchecked((int)0x887A0004));
        }

        private byte[] PackScreenshotFrame(CapturedFrameSet frames, CancellationToken token)
        {
            return PackSdrFrame(frames.Hdr is { } hdr
                ? HdrToneMapping.Map(hdr, _screenshotToneMap, token)
                : frames.Sdr);
        }

        private void UpdateHdrAvailability()
        {
            if (Annotations is not { } annotations) return;
            if (ScreenshotPreview && !_historyPreviewHdr)
            {
                annotations.CanOutputHdr = false;
                return;
            }

            if (_screenshots.Count == 0) return;
            var region = Snapshot.Region;
            if (region.IsEmpty || NeedsWindowScreenshot) return;
            if (WindowSource && !UsesLocalSource)
            {
                annotations.CanOutputHdr = _retainHdr && _frozenWindow is { Hdr: { } hdr } window &&
                                           window.Target.Handle == Snapshot.Target?.Handle &&
                                           HasHdrRegion(hdr, window.Region, region);
                return;
            }

            var hasHdr = false;
            foreach (var item in _hdrScreenshots)
            {
                if (item.Region.Intersect(region).IsEmpty) continue;
                if (item.Frame is null)
                {
                    annotations.CanOutputHdr = false;
                    return;
                }

                hasHdr |= HasHdrRegion(item.Frame, item.Region, region);
            }

            annotations.CanOutputHdr = (_retainHdr || _historyPreviewHdr) && hasHdr;
        }

        private static bool HasHdrRegion(CapturedFrame frame, PixelRect bounds, PixelRect region)
        {
            if (bounds.Intersect(region).IsEmpty) return false;
            if (frame.ColorRegions.Count == 0) return frame.ColorInfo.IsHdrSource;
            foreach (var part in frame.ColorRegions)
                if (part.ColorInfo.IsHdrSource &&
                    !new PixelRect(bounds.X + part.Region.X, bounds.Y + part.Region.Y,
                        part.Region.Width, part.Region.Height).Intersect(region).IsEmpty)
                    return true;
            return false;
        }

        private (CapturedFrame? Frame, PixelRect? Bounds, string? Reason) ExportHdrSamples(SelectionResult selection,
            CancellationToken token)
        {
            if (selection.WindowSource && !UsesLocalSource && _frozenWindow is { Hdr: { } hdr } window)
                return (hdr, window.Region, null);
            if (_hdrScreenshots.Count == 0) return (null, null, UiText.Get("HdrCaptureUnavailable"));
            var left = _hdrScreenshots.Min(item => item.Region.X);
            var top = _hdrScreenshots.Min(item => item.Region.Y);
            var bounds = new PixelRect(left, top, checked(_hdrScreenshots.Max(item => item.Region.Right) - left),
                checked(_hdrScreenshots.Max(item => item.Region.Bottom) - top));
            var (frame, reason) = ExportHdrScreenshot(selection with { Region = bounds, Cursor = false }, token);
            return (frame, bounds, reason);
        }

        private static CapturedFrameSet CreateSdrFrames(int width, int height, byte[] pixels)
        {
            return new(new(width, height, checked(width * 4), CapturePixelFormat.Bgra8Srgb, pixels,
                new(false), CaptureAlphaMode.Opaque, -1, []), null, Stopwatch.GetTimestamp());
        }

        private static byte[] PackSdrFrame(CapturedFrame frame)
        {
            if (frame.PixelFormat != CapturePixelFormat.Bgra8Srgb || frame.Stride < checked(frame.Width * 4) ||
                frame.Pixels.Length < checked(frame.Stride * frame.Height))
                throw new InvalidOperationException(UiText.Get("InvalidFrameBuffer"));
            if (frame.Stride == frame.Width * 4 &&
                MemoryMarshal.TryGetArray(frame.Pixels, out var buffer) &&
                buffer.Offset == 0 && buffer.Count == buffer.Array!.Length)
                return buffer.Array;
            var pixels = new byte[checked(frame.Width * frame.Height * 4)];
            for (var row = 0; row < frame.Height; row++)
                frame.Pixels.Span.Slice(row * frame.Stride, frame.Width * 4)
                    .CopyTo(pixels.AsSpan(row * frame.Width * 4, frame.Width * 4));
            return pixels;
        }

        private (CapturedFrame? Frame, string? Reason) ExportHdrScreenshot(SelectionResult selection,
            CancellationToken token)
        {
            if (!_retainHdr && !_historyPreviewHdr) return (null, UiText.Get("HdrCaptureUnavailable"));
            if (ScreenshotPreview && !_historyPreviewHdr)
                return (null, UiText.Get("HdrScreenshotScrollingUnsupported"));
            var region = selection.Region;
            var frames = selection.WindowSource && !UsesLocalSource && _frozenWindow is { } window
                ? [(window.Region, window.Hdr)]
                : _hdrScreenshots;
            var intersecting = frames.Where(item => !item.Region.Intersect(region).IsEmpty).ToArray();
            if (intersecting.Length == 0 || intersecting.Any(item => item.Frame is null) ||
                !intersecting.Any(item => item.Frame!.ColorRegions.Count == 0
                    ? item.Frame.ColorInfo.IsHdrSource
                    : item.Frame.ColorRegions.Any(part => part.ColorInfo.IsHdrSource &&
                                                          !new PixelRect(item.Region.X + part.Region.X,
                                                                  item.Region.Y + part.Region.Y,
                                                                  part.Region.Width, part.Region.Height)
                                                              .Intersect(region)
                                                              .IsEmpty)))
                return (null, UiText.Get("HdrCaptureUnavailable"));
            var pixels = new byte[checked(region.Width * region.Height * 8)];
            for (var alpha = 6; alpha < pixels.Length; alpha += 8)
                BinaryPrimitives.WriteHalfLittleEndian(pixels.AsSpan(alpha, 2), (Half)1);
            var colors = new List<FrameColorRegion>();
            foreach (var item in intersecting)
            {
                token.ThrowIfCancellationRequested();
                var frame = item.Frame!;
                if (frame.PixelFormat != CapturePixelFormat.Rgba16FloatScRgb || frame.Width != item.Region.Width ||
                    frame.Height != item.Region.Height || frame.Stride < checked(frame.Width * 8) ||
                    frame.Pixels.Length < checked(frame.Stride * frame.Height))
                    return (null, UiText.Get("HdrCaptureUnavailable"));
                var clipped = item.Region.Intersect(region);
                for (var row = 0; row < clipped.Height; row++)
                {
                    token.ThrowIfCancellationRequested();
                    var source = (clipped.Y - item.Region.Y + row) * frame.Stride + (clipped.X - item.Region.X) * 8;
                    var destination = ((clipped.Y - region.Y + row) * region.Width + clipped.X - region.X) * 8;
                    frame.Pixels.Span.Slice(source, clipped.Width * 8)
                        .CopyTo(pixels.AsSpan(destination, clipped.Width * 8));
                    var values = MemoryMarshal.Cast<byte, Half>(pixels.AsSpan(destination, clipped.Width * 8));
                    switch (frame.AlphaMode)
                    {
                        case CaptureAlphaMode.Opaque:
                            for (var index = 3; index < values.Length; index += 4) values[index] = (Half)1;
                            break;
                        case CaptureAlphaMode.Straight:
                            for (var index = 0; index < values.Length; index += 4)
                            for (var channel = 0; channel < 3; channel++)
                                values[index + channel] =
                                    (Half)((float)values[index + channel] * (float)values[index + 3]);
                            break;
                        case CaptureAlphaMode.Premultiplied:
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(selection));
                    }
                }

                var parts = frame.ColorRegions.Count == 0
                    ? [new(new(0, 0, frame.Width, frame.Height), frame.ColorInfo)]
                    : frame.ColorRegions;
                foreach (var part in parts)
                {
                    var bounds = new PixelRect(item.Region.X + part.Region.X, item.Region.Y + part.Region.Y,
                        part.Region.Width, part.Region.Height).Intersect(clipped);
                    if (!bounds.IsEmpty)
                        colors.Add(part with
                        {
                            Region = bounds with
                            {
                                X = bounds.X - region.X, Y = bounds.Y - region.Y,
                            },
                        });
                }
            }

            return (new(region.Width, region.Height, checked(region.Width * 8), CapturePixelFormat.Rgba16FloatScRgb,
                pixels, new(true), CaptureAlphaMode.Premultiplied, intersecting.Max(item => item.Frame!.Revision),
                colors), null);
        }
    }
}

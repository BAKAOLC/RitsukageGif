using ImageMagick;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SelectionCoordinator
    {
        private readonly List<(PixelRect Region, CapturedFrameSet Frames)> _nativeScreenshots = [];

        private (PixelRect Bounds, CapturedFrameSet Frames)[] NativeSources(SelectionResult result)
        {
            if (ScreenshotPreview) return [];
            if (result.WindowSource && !UsesLocalSource)
                return _frozenWindow is { NativeFrames: { } native } window ? [(window.Region, native)] : [];
            return [.. _nativeScreenshots];
        }

        private CapturedFrameSet? NativeSelectedFrames(SelectionResult result, CancellationToken token)
        {
            var sources = NativeSources(result);
            var intersecting = sources.Where(item => !item.Bounds.Intersect(result.Region).IsEmpty).ToArray();
            if (intersecting.Length == 0) return null;
            var density = intersecting.Max(item => Math.Max((double)item.Frames.Sdr.Width / item.Bounds.Width,
                (double)item.Frames.Sdr.Height / item.Bounds.Height));
            return NativeFrameComposition.Compose(intersecting, result.Region, density, token);
        }

        private (CapturedFrameSet? Frame, PixelRect Bounds) NativeSampleFrames(SelectionResult result,
            CancellationToken token)
        {
            var sources = NativeSources(result);
            if (sources.Length == 0) return (null, default);
            var left = sources.Min(item => item.Bounds.X);
            var top = sources.Min(item => item.Bounds.Y);
            var right = sources.Max(item => item.Bounds.Right);
            var bottom = sources.Max(item => item.Bounds.Bottom);
            var bounds = new PixelRect(left, top, right - left, bottom - top);
            var density = sources.Max(item => Math.Max((double)item.Frames.Sdr.Width / item.Bounds.Width,
                (double)item.Frames.Sdr.Height / item.Bounds.Height));
            return (NativeFrameComposition.Compose(sources, bounds, density, token), bounds);
        }

        private async Task<byte[]?> ComposeNativeScreenshotAsync(SelectionResult result,
            ScreenshotAnnotationSession annotations, CancellationToken token)
        {
            var frames = await Task.Run(() => NativeSelectedFrames(result, token), token);
            if (frames is null) return null;
            var source = frames.Hdr is { } rawHdr ? HdrToneMapping.Map(rawHdr, _screenshotToneMap, token) : frames.Sdr;
            if (result.Cursor && ScreenshotCursor is { } cursor)
            {
                var pixels = CapturedFramePixels.ToStraightBgra(source, token).ToArray();
                cursor.DrawScaled(pixels, result.Region, source.Width, source.Height);
                source = source with { Pixels = pixels, AlphaMode = CaptureAlphaMode.Straight };
            }

            var samples = annotations.HasMosaicAnnotations
                ? await Task.Run(() => NativeSampleFrames(result, token), token)
                : (Frame: null, Bounds: default);
            var composed = await annotations.ComposeNativeSdrAsync(result.Region, source, samples.Frame?.Sdr,
                samples.Frame is null ? null : samples.Bounds, token);
            return await Task.Run(() => EncodeNativePng(composed, token), token);
        }

        private async Task<(CapturedFrame? Frame, string? Reason)> ComposeNativeHdrScreenshotAsync(
            SelectionResult result, ScreenshotAnnotationSession annotations, CancellationToken token)
        {
            var frames = await Task.Run(() => NativeSelectedFrames(result, token), token);
            if (frames?.Hdr is not { } source) return (null, UiText.Get("HdrCaptureUnavailable"));
            if (result.Cursor && ScreenshotCursor is { } cursor)
            {
                var overlay = new byte[checked(source.Width * source.Height * 4)];
                cursor.DrawScaled(overlay, result.Region, source.Width, source.Height);
                var output = source.Pixels.ToArray();
                await Task.Run(() => HdrAnnotationBlender.Blend(output, source,
                    new(0, 0, source.Width, source.Height), overlay, source.Width * 4,
                    CaptureAlphaMode.Straight, token), token);
                source = source with { Pixels = output };
            }

            var samples = annotations.HasMosaicAnnotations
                ? await Task.Run(() => NativeSampleFrames(result, token), token)
                : (Frame: null, Bounds: default);
            var composed = await annotations.ComposeNativeHdrAsync(result.Region, source, samples.Frame?.Hdr,
                samples.Frame?.Hdr is null ? null : samples.Bounds, token);
            if (result.Shape is { } shape)
                composed = await Task.Run(() => SelectionShapeMask.Apply(composed, shape, result.Region, token), token);
            return (composed, null);
        }

        private static byte[] EncodeNativePng(CapturedFrame source, CancellationToken token)
        {
            var pixels = CapturedFramePixels.ToStraightBgra(source, token);
            using var image = new MagickImage(pixels, new PixelReadSettings((uint)source.Width,
                (uint)source.Height, StorageType.Char, PixelMapping.BGRA));
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
    }
}

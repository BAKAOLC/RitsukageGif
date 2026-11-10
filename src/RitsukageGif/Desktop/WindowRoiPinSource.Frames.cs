using System.Buffers;

namespace RitsukageGif.Desktop
{
    internal sealed partial class WindowRoiPinSource
    {
        private static WindowRoiPinFrame CreateFrame(CapturedFrameSet frames, PixelRect bounds,
            WindowRoiPinDescriptor descriptor, CancellationToken token,
            HdrToneMapMode toneMap = HdrToneMapMode.HighlightCompression,
            PixelRect? coordinateBounds = null, double outputDensity = 1, double? nativePixelDensity = null)
        {
            token.ThrowIfCancellationRequested();
            var region = descriptor.RelativeRegion;
            var actualBounds = coordinateBounds ?? bounds;
            var globalRegion = region with { X = checked(bounds.X + region.X), Y = checked(bounds.Y + region.Y) };
            var rawWidth = checked((int)Math.Ceiling(region.Width * outputDensity));
            var rawHeight = checked((int)Math.Ceiling(region.Height * outputDensity));
            var cropX = (globalRegion.X - actualBounds.X) * outputDensity;
            var cropY = (globalRegion.Y - actualBounds.Y) * outputDensity;
            var direct = (actualBounds.Width * outputDensity).Equals(frames.Sdr.Width) &&
                         (actualBounds.Height * outputDensity).Equals(frames.Sdr.Height) &&
                         double.IsInteger(cropX) && double.IsInteger(cropY) &&
                         (frames.Hdr is null || (frames.Hdr.Width == frames.Sdr.Width &&
                                                 frames.Hdr.Height == frames.Sdr.Height));
            if (!direct)
            {
                frames = NativeFrameComposition.Compose([(actualBounds, frames)], globalRegion, outputDensity, token);
                cropX = 0;
                cropY = 0;
            }

            var pixelRegion = new PixelRect(checked((int)cropX), checked((int)cropY), rawWidth, rawHeight);
            var swap = (descriptor.QuarterTurns & 1) != 0;
            var width = swap ? rawHeight : rawWidth;
            var height = swap ? rawWidth : rawHeight;
            var pixels = ArrayPool<byte>.Shared.Rent(checked(width * height * 4));
            byte[]? linear = null;
            try
            {
                if (frames.Hdr is null || toneMap == HdrToneMapMode.HighlightCompression)
                    CropTransform(frames.Sdr, pixelRegion, descriptor, 4, pixels.AsSpan(0, width * height * 4), token);
                CapturedFrame? hdr = null;
                if (frames.Hdr is { } captured)
                {
                    linear = ArrayPool<byte>.Shared.Rent(checked(width * height * 8));
                    CropTransform(captured, pixelRegion, descriptor, 8, linear.AsSpan(0, width * height * 8), token);
                    hdr = captured with
                    {
                        Width = width, Height = height, Stride = width * 8,
                        Pixels = linear.AsMemory(0, width * height * 8),
                        ColorRegions = TransformColorRegions(captured, pixelRegion, descriptor),
                    };
                    if (toneMap != HdrToneMapMode.HighlightCompression)
                        HdrToneMapping.Map(hdr, toneMap, pixels, token);
                }

                return WindowRoiPinFrame.FromPooled(width, height, pixels, linear, hdr, nativePixelDensity);
            }
            catch
            {
                ArrayPool<byte>.Shared.Return(pixels);
                if (linear is not null) ArrayPool<byte>.Shared.Return(linear);
                throw;
            }
        }

        private static List<FrameColorRegion> TransformColorRegions(CapturedFrame source,
            PixelRect region, WindowRoiPinDescriptor descriptor)
        {
            var regions = source.ColorRegions;
            if (regions.Count == 0) regions = [new(new(0, 0, source.Width, source.Height), source.ColorInfo)];
            var result = new List<FrameColorRegion>(regions.Count);
            foreach (var item in regions)
            {
                var clipped = item.Region.Intersect(region);
                if (clipped.IsEmpty) continue;
                var first = TransformPoint(clipped.X - region.X, clipped.Y - region.Y, region, descriptor);
                var last = TransformPoint(clipped.Right - region.X - 1, clipped.Bottom - region.Y - 1, region,
                    descriptor);
                result.Add(item with
                {
                    Region = new(Math.Min(first.X, last.X), Math.Min(first.Y, last.Y),
                        Math.Abs(last.X - first.X) + 1, Math.Abs(last.Y - first.Y) + 1),
                });
            }

            return result;
        }

        private static void CropTransform(CapturedFrame source, PixelRect region,
            WindowRoiPinDescriptor descriptor, int bytesPerPixel, Span<byte> result, CancellationToken token)
        {
            var width = (descriptor.QuarterTurns & 1) != 0 ? region.Height : region.Width;
            var clipped = region.Intersect(new(0, 0, source.Width, source.Height));
            if (clipped != region) result.Clear();
            var input = source.Pixels.Span;
            for (var y = clipped.Y; y < clipped.Bottom; y++)
            {
                token.ThrowIfCancellationRequested();
                if (descriptor is { QuarterTurns: 0, FlipHorizontal: false, FlipVertical: false })
                {
                    var rowBytes = checked(clipped.Width * bytesPerPixel);
                    var destination = result.Slice(((y - region.Y) * width + clipped.X - region.X) * bytesPerPixel,
                        rowBytes);
                    input.Slice(y * source.Stride + clipped.X * bytesPerPixel, rowBytes).CopyTo(destination);
                    if (bytesPerPixel == 4) StraightenAlpha(destination, source.AlphaMode);
                    continue;
                }

                for (var x = clipped.X; x < clipped.Right; x++)
                {
                    var point = TransformPoint(x - region.X, y - region.Y, region, descriptor);
                    var destination = result.Slice((point.Y * width + point.X) * bytesPerPixel, bytesPerPixel);
                    input.Slice(y * source.Stride + x * bytesPerPixel, bytesPerPixel).CopyTo(destination);
                    if (bytesPerPixel == 4) StraightenAlpha(destination, source.AlphaMode);
                }
            }
        }

        private static PixelPoint TransformPoint(int x, int y, PixelRect region, WindowRoiPinDescriptor descriptor)
        {
            var swap = (descriptor.QuarterTurns & 1) != 0;
            var width = swap ? region.Height : region.Width;
            var height = swap ? region.Width : region.Height;
            var point = descriptor.QuarterTurns switch
            {
                1 => new PixelPoint(region.Height - 1 - y, x),
                2 => new PixelPoint(region.Width - 1 - x, region.Height - 1 - y),
                3 => new PixelPoint(y, region.Width - 1 - x),
                _ => new PixelPoint(x, y),
            };
            return new(descriptor.FlipHorizontal ? width - 1 - point.X : point.X,
                descriptor.FlipVertical ? height - 1 - point.Y : point.Y);
        }

        private static void StraightenAlpha(Span<byte> pixels, CaptureAlphaMode mode)
        {
            if (mode == CaptureAlphaMode.Straight) return;
            for (var offset = 0; offset < pixels.Length; offset += 4)
                if (mode == CaptureAlphaMode.Opaque)
                {
                    pixels[offset + 3] = 255;
                }
                else
                {
                    var alpha = pixels[offset + 3];
                    for (var channel = 0; channel < 3; channel++)
                        pixels[offset + channel] = alpha == 0
                            ? (byte)0
                            : (byte)Math.Min(255, (pixels[offset + channel] * 255 + alpha / 2) / alpha);
                }
        }
    }
}

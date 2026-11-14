namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotScrollingSession
    {
        // ReSharper disable once UnusedAutoPropertyAccessor.Global
        internal Func<PixelRect, CancellationToken, CapturedFrameSet>? CaptureFrameSet { get; set; }

        public async Task<ScrollCaptureImage> ExportAsync(CancellationToken token)
        {
            ScrollCaptureImage image;
            (PixelRect Area, byte[] Pixels)? overlay;
            lock (_sync)
            {
                image = Export(token);
                if (image.Hdr is null) return image;
                overlay = CursorOverlay(image.Width, image.Height);
            }

            if (overlay is not { } value) return image;
            var hdr = await HdrAnnotationBlender.CompositeAsync(image.Hdr,
                new(0, 0, image.Width, image.Height), value.Pixels, value.Area, token);
            return image with { Hdr = hdr };
        }

        private (PixelRect Area, byte[] Pixels)? CursorOverlay(int width, int height)
        {
            if (_cursor is null || _firstRegion is not { } first) return null;
            var offset = checked(_leadingInset + _stitching.StartOffset);
            var vertical = _axis == ScrollCaptureAxis.Vertical;
            var offsetX = vertical ? 0 : offset;
            var offsetY = vertical ? offset : 0;
            var overlay = _cursor.CreateDensityOverlay(first, _density, offsetX, offsetY, width, height);
            if (overlay is not { } value) return null;
            var content = vertical
                ? new PixelRect(-offsetX, _leadingInset - offsetY, PhysicalSize(first.Width),
                    PhysicalSize(first.Height) - _leadingInset - _trailingInset)
                : new PixelRect(_leadingInset - offsetX, -offsetY,
                    PhysicalSize(first.Width) - _leadingInset - _trailingInset, PhysicalSize(first.Height));
            var clipped = value.Area.Intersect(content).Intersect(new(0, 0, width, height));
            if (clipped.IsEmpty) return null;
            if (clipped == value.Area) return value;
            var pixels = new byte[checked(clipped.Width * clipped.Height * 4)];
            for (var y = 0; y < clipped.Height; y++)
                value.Pixels.AsSpan(((clipped.Y - value.Area.Y + y) * value.Area.Width +
                        clipped.X - value.Area.X) * 4, clipped.Width * 4)
                    .CopyTo(pixels.AsSpan(y * clipped.Width * 4));
            return (clipped, pixels);
        }

        private static void BlendCursor(byte[] output, int width, PixelRect area, byte[] pixels,
            CancellationToken token)
        {
            for (var y = 0; y < area.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                BlendCursorRow(output.AsSpan(((area.Y + y) * width + area.X) * 4, area.Width * 4),
                    pixels.AsSpan(y * area.Width * 4, area.Width * 4));
            }
        }

        private static void BlendCursorRow(Span<byte> output, ReadOnlySpan<byte> pixels)
        {
            for (var offset = 0; offset < pixels.Length; offset += 4)
            {
                var alpha = pixels[offset + 3];
                if (alpha == 0) continue;
                var behindAlpha = output[offset + 3];
                var combined = alpha + (behindAlpha * (255 - alpha) + 127) / 255;
                for (var channel = 0; channel < 3; channel++)
                    output[offset + channel] = (byte)((pixels[offset + channel] * alpha +
                                                       output[offset + channel] * behindAlpha *
                                                       (255 - alpha) / 255 + combined / 2) / combined);
                output[offset + 3] = (byte)combined;
            }
        }
    }
}

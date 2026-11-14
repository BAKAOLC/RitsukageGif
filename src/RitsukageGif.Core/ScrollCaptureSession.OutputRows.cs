namespace RitsukageGif.Core
{
    public sealed partial class ScrollCaptureSession
    {
        public FrameColorInfo OutputHdrColorInfo
        {
            get
            {
                ValidateOutputRegion(new(0, 0, Width, Height), true);
                return OutputColorInfo(ExportHdrRegions(Width, Height, new(0, 0, Width, Height)));
            }
        }

        public void CopyOutputRow(int y, Span<byte> target, bool hdr = false, CancellationToken token = default)
        {
            ValidateOutputRegion(new(0, y, Width, 1), hdr);
            var pixelBytes = hdr ? 8 : 4;
            if (target.Length != checked(Width * pixelBytes))
                throw new ArgumentException("The output row size does not match the capture.", nameof(target));
            CopyOutputRow(y, 0, Width, pixelBytes, target, token);
        }

        public byte[] ReadOutputSdrRegion(PixelRect region, CancellationToken token = default)
        {
            ValidateOutputRegion(region, false);
            return ReadOutputRegion(region, 4, token);
        }

        public CapturedFrame ReadOutputHdrRegion(PixelRect region, CancellationToken token = default)
        {
            ValidateOutputRegion(region, true);
            var pixels = ReadOutputRegion(region, 8, token);
            var regions = ExportHdrRegions(Width, Height, region);
            return NewHdrFrame(region.Width, region.Height, pixels, regions);
        }

        private void ValidateOutputRegion(PixelRect region, bool hdr)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (region.IsEmpty || region.X < 0 || region.Y < 0 || (long)region.X + region.Width > Width ||
                (long)region.Y + region.Height > Height || _strips.Count == 0)
                throw new ArgumentOutOfRangeException(nameof(region));
            if (hdr && !_options.RetainHdr) throw new InvalidOperationException("No HDR pixels were retained.");
        }

        private byte[] ReadOutputRegion(PixelRect region, int pixelBytes, CancellationToken token)
        {
            var stride = checked(region.Width * pixelBytes);
            var pixels = new byte[checked(stride * region.Height)];
            for (var y = 0; y < region.Height; y++)
                CopyOutputRow(region.Y + y, region.X, region.Width, pixelBytes, pixels.AsSpan(y * stride, stride),
                    token);
            return pixels;
        }

        private void CopyOutputRow(int y, int x, int width, int pixelBytes, Span<byte> target, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var hdr = pixelBytes == 8;
            if (_options.Axis == ScrollCaptureAxis.Vertical)
            {
                var position = StartOffset + y;
                ReadRow(FindStrip(position), position, hdr).Slice(x * pixelBytes, width * pixelBytes).CopyTo(target);
                return;
            }

            var left = StartOffset + x;
            var right = left + width;
            foreach (var strip in _strips)
            {
                token.ThrowIfCancellationRequested();
                if (strip.End <= left) continue;
                if (strip.Start >= right) break;
                var start = Math.Max(strip.Start, left);
                var columns = Math.Min(strip.End, right) - start;
                var destination = target.Slice((start - left) * pixelBytes, columns * pixelBytes);
                if (strip.Disk is { } disk)
                {
                    var originalWidth = disk.SdrLength / _rowBytes;
                    var offset = disk.Position + disk.LogicalLength + (hdr ? disk.SdrLength : 0);
                    var skip = strip.DataOffset / _rowBytes + start - strip.Start;
                    ReadDisk(destination, offset + ((long)y * originalWidth + skip) * pixelBytes);
                }
                else
                {
                    for (var column = 0; column < columns; column++)
                        ReadRow(strip, start + column, hdr).Slice(y * pixelBytes, pixelBytes)
                            .CopyTo(destination.Slice(column * pixelBytes, pixelBytes));
                }
            }
        }
    }
}

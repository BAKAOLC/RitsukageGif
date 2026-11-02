using System.Buffers.Binary;

namespace RitsukageGif.Core
{
    public sealed partial class ScrollCaptureSession
    {
        private long _hdrRevision;

        private void ValidateHdr(CapturedFrame? frame)
        {
            if (frame is null)
                throw new NotSupportedException("The scrolling capture source no longer provides HDR pixels.");
            if (frame.PixelFormat != CapturePixelFormat.Rgba16FloatScRgb || frame.Width != _width ||
                frame.Height != _height || frame.Stride < (long)_width * 8 ||
                frame.Pixels.Length < (long)frame.Stride * _height || !Enum.IsDefined(frame.AlphaMode))
                throw new ArgumentException("The HDR frame format or dimensions are invalid.", nameof(frame));
        }

        private Strip CreateStrip(int begin, int end, int offset, byte[] pixels, CapturedFrame? hdr,
            CancellationToken token)
        {
            var sdr = pixels.AsSpan((begin - offset) * _rowBytes, (end - begin) * _rowBytes).ToArray();
            if (!_options.RetainHdr) return new(begin, end, sdr);
            var source = hdr!;
            var vertical = _options.Axis == ScrollCaptureAxis.Vertical;
            var rowBytes = checked(_crossLength * 8);
            var output = new byte[checked((end - begin) * rowBytes)];
            for (var axis = begin; axis < end; axis++)
            {
                token.ThrowIfCancellationRequested();
                var sourceAxis = axis - offset + _options.LeadingInset;
                if (vertical)
                    source.Pixels.Span.Slice(sourceAxis * source.Stride, rowBytes)
                        .CopyTo(output.AsSpan((axis - begin) * rowBytes, rowBytes));
                else
                    for (var cross = 0; cross < _crossLength; cross++)
                        source.Pixels.Span.Slice(cross * source.Stride + sourceAxis * 8, 8)
                            .CopyTo(output.AsSpan((axis - begin) * rowBytes + cross * 8, 8));
            }

            if (source.AlphaMode != CaptureAlphaMode.Premultiplied)
                for (var position = 0; position < output.Length; position += 8)
                {
                    if ((position & 32767) == 0) token.ThrowIfCancellationRequested();
                    var pixel = output.AsSpan(position, 8);
                    if (source.AlphaMode == CaptureAlphaMode.Opaque)
                    {
                        BinaryPrimitives.WriteInt16LittleEndian(pixel[6..], BitConverter.HalfToInt16Bits((Half)1));
                        continue;
                    }

                    var alpha = (float)BitConverter.Int16BitsToHalf(BinaryPrimitives.ReadInt16LittleEndian(pixel[6..]));
                    if (alpha.Equals(1f)) continue;
                    for (var channel = 0; channel < 3; channel++)
                    {
                        var component = pixel.Slice(channel * 2, 2);
                        var value = (float)BitConverter.Int16BitsToHalf(
                            BinaryPrimitives.ReadInt16LittleEndian(component));
                        BinaryPrimitives.WriteInt16LittleEndian(component,
                            BitConverter.HalfToInt16Bits((Half)(value * alpha)));
                    }
                }

            var regions = new List<FrameColorRegion>();
            var stripBounds = vertical
                ? new PixelRect(0, begin, _crossLength, end - begin)
                : new PixelRect(begin, 0, end - begin, _crossLength);
            if (source.ColorRegions.Count == 0) regions.Add(new(stripBounds, source.ColorInfo));
            else
                foreach (var item in source.ColorRegions)
                {
                    var bounds = item.Region;
                    var translated = vertical
                        ? new PixelRect(bounds.X, bounds.Y + offset - _options.LeadingInset, bounds.Width,
                            bounds.Height)
                        : new PixelRect(bounds.X + offset - _options.LeadingInset, bounds.Y, bounds.Width,
                            bounds.Height);
                    var clipped = translated.Intersect(stripBounds);
                    if (!clipped.IsEmpty) regions.Add(item with { Region = clipped });
                }

            _hdrRevision = Math.Max(_hdrRevision, source.Revision);
            return new(begin, end, sdr, HdrPixels: output, HdrRegions: regions);
        }

        private CapturedFrame ExportHdr(int width, int height, CancellationToken token)
        {
            var output = new byte[checked(width * height * 8)];
            var vertical = _options.Axis == ScrollCaptureAxis.Vertical;
            var stripIndex = 0;
            if (width == Width && height == Height)
                foreach (var strip in _strips)
                {
                    token.ThrowIfCancellationRequested();
                    if (vertical)
                        strip.HdrPixels!.AsSpan(strip.DataOffset * 2, (strip.End - strip.Start) * _crossLength * 8)
                            .CopyTo(output.AsSpan((strip.Start - StartOffset) * _crossLength * 8));
                    else
                        for (var axis = strip.Start; axis < strip.End; axis++)
                        for (var cross = 0; cross < _crossLength; cross++)
                            strip.HdrPixels!.AsSpan(strip.DataOffset * 2 +
                                                    ((axis - strip.Start) * _crossLength + cross) * 8, 8)
                                .CopyTo(output.AsSpan((cross * width + axis - StartOffset) * 8, 8));
                }
            else
                for (var y = 0; y < height; y++)
                {
                    token.ThrowIfCancellationRequested();
                    var sourceY = (int)((long)y * Height / height);
                    for (var x = 0; x < width; x++)
                    {
                        var sourceX = (int)((long)x * Width / width);
                        var axis = StartOffset + (vertical ? sourceY : sourceX);
                        while (stripIndex > 0 && axis < _strips[stripIndex].Start) stripIndex--;
                        while (axis >= _strips[stripIndex].End) stripIndex++;
                        var strip = _strips[stripIndex];
                        var sourceOffset = strip.DataOffset * 2 +
                                           ((axis - strip.Start) * _crossLength + (vertical ? sourceX : sourceY)) * 8;
                        strip.HdrPixels!.AsSpan(sourceOffset, 8).CopyTo(output.AsSpan((y * width + x) * 8, 8));
                    }
                }

            var regions = new List<FrameColorRegion>();
            var outputBounds = vertical
                ? new PixelRect(0, StartOffset, Width, Height)
                : new PixelRect(StartOffset, 0, Width, Height);
            // ReSharper disable once ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator
            foreach (var strip in _strips)
            foreach (var item in strip.HdrRegions!)
            {
                var retainedBounds = vertical
                    ? new PixelRect(0, strip.Start, _crossLength, strip.End - strip.Start)
                    : new PixelRect(strip.Start, 0, strip.End - strip.Start, _crossLength);
                var clipped = item.Region.Intersect(retainedBounds).Intersect(outputBounds);
                if (clipped.IsEmpty) continue;
                var x = clipped.X - outputBounds.X;
                var y = clipped.Y - outputBounds.Y;
                var left = (int)(((long)x * width + Width - 1) / Width);
                var top = (int)(((long)y * height + Height - 1) / Height);
                var right = (int)(((long)(x + clipped.Width) * width + Width - 1) / Width);
                var bottom = (int)(((long)(y + clipped.Height) * height + Height - 1) / Height);
                if (right > left && bottom > top)
                    regions.Add(item with { Region = new(left, top, right - left, bottom - top) });
            }

            var hasHdrSource = regions.Any(region => region.ColorInfo.IsHdrSource);
            var color = regions.Count == 0
                ? new(hasHdrSource)
                : regions[0].ColorInfo with { IsHdrSource = hasHdrSource };
            return new(width, height, checked(width * 8), CapturePixelFormat.Rgba16FloatScRgb, output,
                color, CaptureAlphaMode.Premultiplied, _hdrRevision, regions);
        }
    }
}

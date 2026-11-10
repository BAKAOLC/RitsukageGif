using System.Buffers.Binary;
using SkiaSharp;

namespace RitsukageGif.Desktop
{
    internal static class RecordingXorCursorOverlay
    {
        // ReSharper disable once UnusedMember.Global
        internal static PreviewFrame Apply(PreviewFrame source, RecordingInputTraceFrame frame,
            RecordingInputTraceFrame? next, TimeSpan position, double size,
            PixelRect crop, CancellationToken token)
        {
            using var buffer = new RecordingOverlayBuffer(source);
            ApplyTo(buffer, frame, next, position, size, crop, token);
            return buffer.TakeResult();
        }

        internal static void ApplyTo(RecordingOverlayBuffer buffer, RecordingInputTraceFrame frame,
            RecordingInputTraceFrame? next, TimeSpan position, double size, PixelRect crop, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var source = buffer.Current;
            var cursor = frame.Cursor;
            if (!cursor.Visible || cursor.AndMask is not { } mask) return;
            byte[]? pixels = null, hdr = null;
            for (var index = 0; index < frame.Geometry.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                var geometry = frame.Geometry[index];
                if (!geometry.OwnsCursor ||
                    !RecordingInputOverlay.Contains(geometry.Region, cursor.X, cursor.Y)) continue;
                var scale = size / (100d * cursor.PixelScale);
                var sx = geometry.Destination.Width / (double)geometry.Region.Width;
                var sy = geometry.Destination.Height / (double)geometry.Region.Height;
                var anchorX = geometry.Destination.X + (cursor.X - geometry.Region.X) * sx;
                var anchorY = geometry.Destination.Y + (cursor.Y - geometry.Region.Y) * sy;
                if (next is not null && next.Position > frame.Position && next.Cursor.Visible &&
                    next.Cursor.ShapeId == cursor.ShapeId && next.Geometry.Count > index &&
                    next.CaptureSequence != frame.CaptureSequence)
                {
                    var following = next.Geometry[index];
                    if (following.OwnsCursor)
                    {
                        var fraction = Math.Clamp((position - frame.Position).Ticks /
                                                  (double)(next.Position - frame.Position).Ticks, 0, 1);
                        var targetX = following.Destination.X + (next.Cursor.X - following.Region.X) *
                            following.Destination.Width / following.Region.Width;
                        var targetY = following.Destination.Y + (next.Cursor.Y - following.Region.Y) *
                            following.Destination.Height / following.Region.Height;
                        anchorX += (targetX - anchorX) * fraction;
                        anchorY += (targetY - anchorY) * fraction;
                    }
                }

                var left = anchorX - cursor.HotspotX * scale * sx;
                var top = anchorY - cursor.HotspotY * scale * sy;
                var width = cursor.Width * scale * sx;
                var height = cursor.Height * scale * sy;
                var previewScaleX = source.Width / (double)crop.Width;
                var previewScaleY = source.Height / (double)crop.Height;
                var firstX = Math.Max(0, (int)Math.Floor((left - crop.X) * previewScaleX));
                var firstY = Math.Max(0, (int)Math.Floor((top - crop.Y) * previewScaleY));
                var lastX = Math.Min(source.Width, (int)Math.Ceiling((left + width - crop.X) * previewScaleX));
                var lastY = Math.Min(source.Height, (int)Math.Ceiling((top + height - crop.Y) * previewScaleY));
                if (firstX >= lastX || firstY >= lastY) continue;
                using var path = geometry.ClipPathData is null ? null : SKPath.ParseSvgPathData(geometry.ClipPathData);
                path?.FillType = geometry.ClipEvenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding;
                if (pixels is null)
                {
                    var target = buffer.Writable();
                    pixels = target.Pixels;
                    hdr = target.HdrPixels;
                }

                for (var y = firstY; y < lastY; y++)
                {
                    token.ThrowIfCancellationRequested();
                    for (var x = firstX; x < lastX; x++)
                    {
                        var rawX = crop.X + (x + 0.5) / previewScaleX;
                        var rawY = crop.Y + (y + 0.5) / previewScaleY;
                        if (rawX < geometry.Destination.X || rawX >= geometry.Destination.Right ||
                            rawY < geometry.Destination.Y || rawY >= geometry.Destination.Bottom ||
                            (path is not null && !path.Contains((float)rawX, (float)rawY))) continue;
                        var u = Math.Clamp((int)((rawX - left) * cursor.Width / width), 0, cursor.Width - 1);
                        var v = Math.Clamp((int)((rawY - top) * cursor.Height / height), 0, cursor.Height - 1);
                        var input = (v * cursor.Width + u) * 4;
                        var output = (y * source.Width + x) * 4;
                        if (mask[input] == 255 && mask[input + 1] == 255 && mask[input + 2] == 255 &&
                            cursor.Pixels[input] == 0 && cursor.Pixels[input + 1] == 0 &&
                            cursor.Pixels[input + 2] == 0) continue;
                        var alpha = pixels[output + 3] / 255d;
                        for (var channel = 0; channel < 3; channel++)
                        {
                            var straight = alpha > 0
                                ? Math.Clamp((int)Math.Round(pixels[output + channel] / alpha), 0, 255)
                                : 0;
                            var changed = (straight & mask[input + channel]) ^ cursor.Pixels[input + channel];
                            pixels[output + channel] = (byte)changed;
                        }

                        pixels[output + 3] = 255;
                        if (hdr is not null)
                            ApplyHdr(hdr, output * 2, cursor, input, mask,
                                source.HdrReferenceWhiteLevel, source.HdrAlphaMode);
                    }
                }
            }
        }

        private static void ApplyHdr(byte[] output, int offset, RecordingCursorSample cursor, int input,
            byte[] mask, float white, CaptureAlphaMode alphaMode)
        {
            var alpha = alphaMode == CaptureAlphaMode.Opaque
                ? 1f
                : (float)BitConverter.UInt16BitsToHalf(
                    BinaryPrimitives.ReadUInt16LittleEndian(output.AsSpan(offset + 6)));
            for (var channel = 0; channel < 3; channel++)
            {
                var cursorChannel = 2 - channel;
                var original = (float)BitConverter.UInt16BitsToHalf(
                    BinaryPrimitives.ReadUInt16LittleEndian(output.AsSpan(offset + channel * 2)));
                var straight = alphaMode == CaptureAlphaMode.Premultiplied && alpha > 0 ? original / alpha : original;
                if (mask[input + cursorChannel] == 255 && cursor.Pixels[input + cursorChannel] == 0)
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(offset + channel * 2),
                        BitConverter.HalfToUInt16Bits((Half)straight));
                    continue;
                }

                var linear = Math.Clamp(straight / white, 0, 1);
                var encoded = linear <= 0.0031308 ? 12.92 * linear : 1.055 * Math.Pow(linear, 1 / 2.4) - 0.055;
                var changed = ((int)Math.Round(encoded * 255) & mask[input + cursorChannel]) ^
                              cursor.Pixels[input + cursorChannel];
                var normalized = changed / 255d;
                var result =
                    (normalized <= 0.04045 ? normalized / 12.92 : Math.Pow((normalized + 0.055) / 1.055, 2.4)) * white;
                BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(offset + channel * 2),
                    BitConverter.HalfToUInt16Bits((Half)result));
            }

            BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(offset + 6), BitConverter.HalfToUInt16Bits((Half)1));
        }
    }
}

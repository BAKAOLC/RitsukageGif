using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace RitsukageGif.Core
{
    internal sealed class HdrVideoFrameSource(
        IFrameSource owner,
        IColorFrameSource source,
        VideoRecordingOptions options) : IFrameSource
    {
        private static readonly ushort[] PqValues = CreatePqValues();
        private byte[]? _sdrBuffer;

        public byte[] Capture()
        {
            return Capture(CancellationToken.None);
        }

        public byte[] Capture(CancellationToken token)
        {
            return Capture(null, token);
        }

        public void Dispose()
        {
            owner.Dispose();
        }

        internal byte[] Capture(byte[]? destination, CancellationToken token)
        {
            var frames = source.CaptureFrames(token);
            var hdr = frames.Hdr ?? throw new NotSupportedException(UiText.Get("HdrRecordingUnavailable"));
            if (hdr.PixelFormat != CapturePixelFormat.Rgba16FloatScRgb ||
                hdr.Width != options.Capture.Width || hdr.Height != options.Capture.Height ||
                hdr.Stride < checked(hdr.Width * 8) || hdr.Pixels.Length < (long)hdr.Stride * hdr.Height)
                throw new InvalidDataException(UiText.Get("InvalidFrameBuffer"));
            if (!hdr.ColorInfo.IsHdrSource && !hdr.ColorRegions.Any(region => region.ColorInfo.IsHdrSource))
                throw new NotSupportedException(UiText.Get("HdrRecordingUnavailable"));
            var both = options.ColorMode == OutputColorMode.Both;
            if (both) _sdrBuffer ??= new byte[checked(hdr.Width * hdr.Height * 4)];
            var sdr = both ? HdrToneMapping.Map(hdr, options.ToneMap, _sdrBuffer, token) : null;
            var stride = checked(hdr.Width * 6 * (both ? 2 : 1));
            var length = checked(stride * hdr.Height);
            var output = destination is not null && destination.Length == length ? destination : new byte[length];
            Parallel.For(0, hdr.Height, new() { CancellationToken = token }, y =>
            {
                var input = MemoryMarshal.Cast<byte, Half>(hdr.Pixels.Span.Slice(y * hdr.Stride, hdr.Width * 8));
                var row = output.AsSpan(y * stride, stride);
                for (var x = 0; x < hdr.Width; x++)
                {
                    var alpha = hdr.AlphaMode == CaptureAlphaMode.Opaque
                        ? 1f
                        : Math.Clamp(Finite((float)input[x * 4 + 3]), 0, 1);
                    var multiplier = hdr.AlphaMode == CaptureAlphaMode.Straight ? alpha : 1;
                    var red = Finite((float)input[x * 4]) * multiplier;
                    var green = Finite((float)input[x * 4 + 1]) * multiplier;
                    var blue = Finite((float)input[x * 4 + 2]) * multiplier;
                    // Linear Rec.709 D65 to linear Rec.2020 D65. scRGB 1.0 is 80 nits.
                    Write(row, x * 6, Pq(.627404f * red + .329283f * green + .043313f * blue));
                    Write(row, x * 6 + 2, Pq(.069097f * red + .919540f * green + .011362f * blue));
                    Write(row, x * 6 + 4, Pq(.016391f * red + .088013f * green + .895595f * blue));
                    if (sdr is null) continue;
                    var pixel = sdr.Pixels.Span.Slice(y * sdr.Stride + x * 4, 4);
                    var opacity = sdr.AlphaMode == CaptureAlphaMode.Opaque ? 255 : pixel[3];
                    var offset = (hdr.Width + x) * 6;
                    Write(row, offset, (ushort)(pixel[2] * opacity * 257 / 255));
                    Write(row, offset + 2, (ushort)(pixel[1] * opacity * 257 / 255));
                    Write(row, offset + 4, (ushort)(pixel[0] * opacity * 257 / 255));
                }
            });
            return output;
        }

        private static float Finite(float value)
        {
            return float.IsFinite(value) ? value : 0;
        }

        private static ushort Pq(float linear)
        {
            return PqValues[BitConverter.HalfToUInt16Bits((Half)linear)];
        }

        private static ushort[] CreatePqValues()
        {
            var values = new ushort[ushort.MaxValue + 1];
            for (var index = 0; index < values.Length; index++)
                values[index] = EncodePq(Finite((float)BitConverter.UInt16BitsToHalf((ushort)index)));
            return values;
        }

        private static ushort EncodePq(float linear)
        {
            var luminance = Math.Clamp(linear * FrameColorInfo.ScRgbReferenceWhiteNits / 10000f, 0, 1);
            var power = MathF.Pow(luminance, 2610f / 16384);
            var encoded = MathF.Pow((3424f / 4096 + 2413f / 128 * power) / (1 + 2392f / 128 * power), 2523f / 32);
            return (ushort)Math.Clamp((int)MathF.Round(encoded * ushort.MaxValue), 0, ushort.MaxValue);
        }

        private static void Write(Span<byte> destination, int offset, ushort value)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(destination[offset..], value);
        }
    }
}

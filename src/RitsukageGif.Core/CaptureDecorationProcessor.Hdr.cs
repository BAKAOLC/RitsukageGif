using System.Buffers.Binary;
using System.Numerics;

namespace RitsukageGif.Core
{
    public static partial class CaptureDecorationProcessor
    {
        public static CapturedFrame Apply(CapturedFrame source, CaptureDecoration decoration,
            CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(source.ColorInfo);
            ArgumentNullException.ThrowIfNull(source.ColorRegions);
            token.ThrowIfCancellationRequested();
            var geometry = Measure(source.Width, source.Height, decoration);
            if (decoration.IsEmpty) return source;
            if (source.PixelFormat != CapturePixelFormat.Rgba16FloatScRgb || source.Stride < (long)source.Width * 8 ||
                source.Pixels.Length < (long)source.Stride * source.Height || !Enum.IsDefined(source.AlphaMode) ||
                !float.IsFinite(source.ColorInfo.SdrWhiteNits) || source.ColorInfo.SdrWhiteNits <= 0)
                throw new ArgumentException(UiText.Get("InvalidFrameBuffer"), nameof(source));
            ValidateBudget(source.Width, source.Height, decoration, source.Pixels.Length, 8);
            var output = new byte[checked(geometry.Width * geometry.Height * 8)];
            ApplyRowsCore(source.Width, source.Height, decoration, ReadAlpha, ReadColor, WriteColor,
                source.ColorInfo.SdrWhiteNits / FrameColorInfo.ScRgbReferenceWhiteNits, token);
            return source with
            {
                Width = geometry.Width, Height = geometry.Height, Stride = checked(geometry.Width * 8),
                Pixels = output, AlphaMode = CaptureAlphaMode.Premultiplied,
                ColorRegions =
                [
                    .. source.ColorRegions.Select(region => region with
                    {
                        Region = region.Region with
                        {
                            X = region.Region.X + geometry.ContentOffset.X,
                            Y = region.Region.Y + geometry.ContentOffset.Y,
                        },
                    }),
                ],
            };

            void ReadAlpha(int y, Span<float> alpha)
            {
                for (var x = 0; x < source.Width; x++) alpha[x] = Alpha(y * source.Stride + x * 8);
            }

            float Alpha(int offset)
            {
                if (source.AlphaMode == CaptureAlphaMode.Opaque) return 1;
                var value = (float)BinaryPrimitives.ReadHalfLittleEndian(source.Pixels.Span[(offset + 6)..]);
                if (!float.IsFinite(value) || value is < 0 or > 1)
                    throw new ArgumentException(UiText.Get("InvalidFrameBuffer"), nameof(source));
                return value;
            }

            void ReadColor(int y, Span<Vector4> row)
            {
                for (var x = 0; x < source.Width; x++)
                {
                    var offset = y * source.Stride + x * 8;
                    var alpha = Alpha(offset);
                    var multiplier = source.AlphaMode == CaptureAlphaMode.Straight ? alpha : 1;
                    var red = (float)BinaryPrimitives.ReadHalfLittleEndian(source.Pixels.Span[offset..]);
                    var green = (float)BinaryPrimitives.ReadHalfLittleEndian(source.Pixels.Span[(offset + 2)..]);
                    var blue = (float)BinaryPrimitives.ReadHalfLittleEndian(source.Pixels.Span[(offset + 4)..]);
                    if (!float.IsFinite(red) || !float.IsFinite(green) || !float.IsFinite(blue))
                        throw new ArgumentException(UiText.Get("InvalidFrameBuffer"), nameof(source));
                    row[x] = new(red * multiplier, green * multiplier, blue * multiplier, alpha);
                }
            }

            void WriteColor(int y, ReadOnlySpan<Vector4> row)
            {
                for (var x = 0; x < geometry.Width; x++)
                {
                    var offset = (y * geometry.Width + x) * 8;
                    BinaryPrimitives.WriteHalfLittleEndian(output.AsSpan(offset), (Half)row[x].X);
                    BinaryPrimitives.WriteHalfLittleEndian(output.AsSpan(offset + 2), (Half)row[x].Y);
                    BinaryPrimitives.WriteHalfLittleEndian(output.AsSpan(offset + 4), (Half)row[x].Z);
                    BinaryPrimitives.WriteHalfLittleEndian(output.AsSpan(offset + 6), (Half)row[x].W);
                }
            }
        }
    }
}

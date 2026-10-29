using System.Buffers.Binary;
using System.Numerics;

namespace RitsukageGif.Core
{
    public static partial class HdrPixelEffectProcessor
    {
        public static CapturedFrame Apply(CapturedFrame frame, IReadOnlyList<PixelEffect> effects,
            CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(frame);
            ArgumentNullException.ThrowIfNull(effects);
            if (frame.PixelFormat != CapturePixelFormat.Rgba16FloatScRgb || frame.Width <= 0 || frame.Height <= 0 ||
                frame.Stride < (long)frame.Width * 8 || (long)frame.Stride * frame.Height > 256L * 1024 * 1024 ||
                frame.Pixels.Length < (long)frame.Stride * frame.Height || !Enum.IsDefined(frame.AlphaMode) ||
                effects.Count > 64)
                throw new ArgumentException(null, nameof(frame));
            foreach (var effect in effects)
            {
                ArgumentNullException.ThrowIfNull(effect);
                effect.Validate();
            }

            token.ThrowIfCancellationRequested();
            if (effects.Count == 0 || effects.All(effect => effect.Amount == 0)) return frame;
            var pixels = frame.Pixels.Span[..checked(frame.Stride * frame.Height)].ToArray();
            var mode = frame.AlphaMode;
            if (mode == CaptureAlphaMode.Opaque)
            {
                // A fill can introduce transparency; materialize the previously implicit opaque alpha.
                mode = CaptureAlphaMode.Straight;
                for (var y = 0; y < frame.Height; y++)
                {
                    token.ThrowIfCancellationRequested();
                    for (var x = 0; x < frame.Width; x++)
                        BinaryPrimitives.WriteUInt16LittleEndian(pixels.AsSpan(y * frame.Stride + x * 8 + 6), 0x3c00);
                }
            }

            foreach (var effect in effects)
            {
                token.ThrowIfCancellationRequested();
                if (effect.Amount == 0) continue;
                var region = Clip(effect.Region ?? new PixelRect(0, 0, frame.Width, frame.Height), frame.Width,
                    frame.Height);
                if (region.IsEmpty) continue;
                switch (effect.Kind)
                {
                    case PixelEffectKind.Blur:
                        Blur(pixels, frame.Stride, mode, region, effect, token);
                        break;
                    case PixelEffectKind.Mosaic:
                        Mosaic(pixels, frame.Stride, mode, region, effect, token);
                        break;
                    case PixelEffectKind.Fill:
                    case PixelEffectKind.Grayscale:
                    case PixelEffectKind.Invert:
                    case PixelEffectKind.BrightnessContrast:
                        Point(pixels, frame.Stride, mode, region, effect, token);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(effects));
                }
            }

            return frame with { Pixels = pixels, AlphaMode = mode };
        }

        private static void Point(byte[] pixels, int stride, CaptureAlphaMode mode, PixelRect area,
            PixelEffect effect, CancellationToken token)
        {
            var alpha = (effect.Color >> 24) / 255f;
            var fill = new Vector4(Linear((effect.Color >> 16) & 255), Linear((effect.Color >> 8) & 255),
                Linear(effect.Color & 255), 1) * alpha;
            for (var y = area.Y; y < area.Bottom; y++)
            {
                token.ThrowIfCancellationRequested();
                for (var x = area.X; x < area.Right; x++)
                {
                    var target = pixels.AsSpan(y * stride + x * 8, 8);
                    var old = Read(target, mode);
                    var straight = old.W > 0 ? old / old.W : Vector4.Zero;
                    straight.W = old.W;
                    Vector4 value;
                    switch (effect.Kind)
                    {
                        case PixelEffectKind.Fill:
                            Write(target, Vector4.Lerp(old, fill, (float)effect.Amount), mode);
                            continue;
                        case PixelEffectKind.Grayscale:
                            var luminance = .2126f * straight.X + .7152f * straight.Y + .0722f * straight.Z;
                            value = new(luminance, luminance, luminance, old.W);
                            break;
                        case PixelEffectKind.Invert:
                            value = new(1 - straight.X, 1 - straight.Y, 1 - straight.Z, old.W);
                            break;
                        case PixelEffectKind.BrightnessContrast:
                            value = (straight - new Vector4(.5f)) * (float)effect.Contrast +
                                    new Vector4(.5f + (float)effect.Brightness);
                            value.W = old.W;
                            break;
                        case PixelEffectKind.Mosaic:
                        case PixelEffectKind.Blur:
                        default:
                            throw new ArgumentOutOfRangeException(nameof(effect));
                    }

                    value.X *= value.W;
                    value.Y *= value.W;
                    value.Z *= value.W;
                    Write(target, Vector4.Lerp(old, value, (float)effect.Amount), mode);
                }
            }
        }

        private static PixelRect Clip(PixelRect value, int width, int height)
        {
            var left = Math.Clamp((long)value.X, 0, width);
            var top = Math.Clamp((long)value.Y, 0, height);
            var right = Math.Clamp((long)value.X + value.Width, 0, width);
            var bottom = Math.Clamp((long)value.Y + value.Height, 0, height);
            return new((int)left, (int)top, (int)Math.Max(0, right - left), (int)Math.Max(0, bottom - top));
        }

        private static float Linear(uint value)
        {
            var encoded = value / 255f;
            return encoded <= .04045f ? encoded / 12.92f : MathF.Pow((encoded + .055f) / 1.055f, 2.4f);
        }

        private static Vector4 Read(ReadOnlySpan<byte> pixel, CaptureAlphaMode mode)
        {
            var value = new Vector4(Sample(pixel), Sample(pixel[2..]), Sample(pixel[4..]),
                Math.Clamp(Sample(pixel[6..]), 0, 1));
            if (mode == CaptureAlphaMode.Straight)
            {
                value.X *= value.W;
                value.Y *= value.W;
                value.Z *= value.W;
            }

            return value;
        }

        private static float Sample(ReadOnlySpan<byte> input)
        {
            var value = (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(input));
            return float.IsFinite(value) ? value : 0;
        }

        private static void Write(Span<byte> output, Vector4 value, CaptureAlphaMode mode)
        {
            value.W = Math.Clamp(value.W, 0, 1);
            if (mode == CaptureAlphaMode.Straight)
            {
                var inverse = value.W > 0 ? 1 / value.W : 0;
                value.X *= inverse;
                value.Y *= inverse;
                value.Z *= inverse;
            }

            Store(output, value.X);
            Store(output[2..], value.Y);
            Store(output[4..], value.Z);
            Store(output[6..], value.W);
        }

        private static void Store(Span<byte> output, float value)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(output,
                BitConverter.HalfToUInt16Bits((Half)Math.Clamp(value, -65504, 65504)));
        }
    }
}

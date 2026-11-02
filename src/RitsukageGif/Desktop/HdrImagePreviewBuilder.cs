using System.Buffers;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal static class HdrImagePreviewBuilder
    {
        internal static PreviewFrame Build(CapturedFrame source, Rect visible, byte[] overlay,
            float displayWhiteLevel, Color? background, CancellationToken token)
        {
            var hdr = ArrayPool<byte>.Shared.Rent(checked(source.Width * source.Height * 8));
            byte[]? sdr = null;
            try
            {
                sdr = ArrayPool<byte>.Shared.Rent(checked(source.Width * source.Height * 4));
                for (var y = 0; y < source.Height; y++)
                {
                    token.ThrowIfCancellationRequested();
                    source.Pixels.Span.Slice(y * source.Stride, source.Width * 8)
                        .CopyTo(hdr.AsSpan(y * source.Width * 8, source.Width * 8));
                }

                var working = source with
                {
                    Pixels = hdr.AsMemory(0, source.Width * source.Height * 8), Stride = source.Width * 8,
                };
                var uiColor = working with
                {
                    ColorInfo = new(false, displayWhiteLevel * FrameColorInfo.ScRgbReferenceWhiteNits),
                    ColorRegions = [],
                };
                HdrAnnotationBlender.Blend(hdr, uiColor, new(0, 0, source.Width, source.Height), overlay,
                    source.Width * 4, CaptureAlphaMode.Straight, token);
                var lightColor = background ?? ImageTransparencyBackground.ColorAt(0, 0);
                var lightLinear = CheckerLinear(lightColor, displayWhiteLevel);
                var darkLinear = CheckerLinear(background ?? ImageTransparencyBackground.ColorAt(8, 0),
                    displayWhiteLevel);
                for (var y = 0; y < source.Height; y++)
                {
                    token.ThrowIfCancellationRequested();
                    var targetRow = hdr.AsSpan(y * source.Width * 8, source.Width * 8);
                    var channels = MemoryMarshal.Cast<byte, Half>(targetRow);
                    for (var x = 0; x < source.Width; x++)
                    {
                        var index = x * 4;
                        var alpha = source.AlphaMode == CaptureAlphaMode.Opaque
                            ? 1
                            : Math.Clamp((float)channels[index + 3], 0, 1);
                        if (alpha >= 1)
                        {
                            channels[index + 3] = (Half)1;
                            continue;
                        }

                        var color = background ?? ImageTransparencyBackground.ColorAt(
                            (int)Math.Floor(visible.X + (x + .5) * visible.Width / source.Width),
                            (int)Math.Floor(visible.Y + (y + .5) * visible.Height / source.Height));
                        var backgroundLinear = color == lightColor ? lightLinear : darkLinear;
                        for (var c = 0; c < 3; c++)
                        {
                            var linear = c switch
                            {
                                0 => backgroundLinear.R, 1 => backgroundLinear.G, _ => backgroundLinear.B,
                            };
                            var original = (float)channels[index + c];
                            channels[index + c] =
                                (Half)(original * (source.AlphaMode == CaptureAlphaMode.Straight ? alpha : 1) +
                                       linear * (1 - alpha));
                        }

                        channels[index + 3] = (Half)1;
                    }
                }

                var flattened = source with
                {
                    Pixels = hdr.AsMemory(0, source.Width * source.Height * 8),
                    Stride = source.Width * 8, AlphaMode = CaptureAlphaMode.Opaque,
                };
                HdrToneMapping.Map(flattened, HdrToneMapMode.HighlightCompression, sdr, token);
                return new(sdr, source.Width, source.Height, 0, hdr,
                    source.ColorInfo.SdrWhiteNits / FrameColorInfo.ScRgbReferenceWhiteNits);
            }
            catch
            {
                ArrayPool<byte>.Shared.Return(hdr);
                if (sdr is not null) ArrayPool<byte>.Shared.Return(sdr);
                throw;
            }
        }

        private static (double R, double G, double B) CheckerLinear(Color color, double white)
        {
            return (Component(color.R) * white, Component(color.G) * white, Component(color.B) * white);

            static double Component(byte value)
            {
                var encoded = value / 255d;
                return encoded <= .04045 ? encoded / 12.92 : Math.Pow((encoded + .055) / 1.055, 2.4);
            }
        }
    }
}

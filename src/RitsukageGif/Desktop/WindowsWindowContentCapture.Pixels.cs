using System.Numerics;
using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal sealed partial class WindowsWindowContentCapture
    {
        private static void ToStraightAlpha(Span<byte> pixels)
        {
            var packed = MemoryMarshal.Cast<byte, uint>(pixels);
            var offset = 0;
            if (Vector.IsHardwareAccelerated)
            {
                var alpha = new Vector<uint>(0xff000000);
                for (; offset <= packed.Length - Vector<uint>.Count; offset += Vector<uint>.Count)
                {
                    var block = new Vector<uint>(packed.Slice(offset, Vector<uint>.Count));
                    if (Vector.EqualsAll(block & alpha, alpha) || Vector.EqualsAll(block, Vector<uint>.Zero))
                        continue;
                    Straighten(pixels.Slice(offset * 4, Vector<uint>.Count * 4));
                }
            }

            Straighten(pixels[(offset * 4)..]);
        }

        private static void Straighten(Span<byte> pixels)
        {
            for (var pixel = 0; pixel < pixels.Length; pixel += 4)
            {
                var alpha = pixels[pixel + 3];
                if (alpha == 255) continue;
                for (var channel = 0; channel < 3; channel++)
                    pixels[pixel + channel] = alpha == 0
                        ? (byte)0
                        : (byte)Math.Min(255, (pixels[pixel + channel] * 255 + alpha / 2) / alpha);
            }
        }
    }
}

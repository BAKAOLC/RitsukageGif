using System.Numerics;

namespace RitsukageGif.Core
{
    public static partial class ImageInpainting
    {
        public static void Restore(Vector4[] pixels, int width, int height, ushort[] mask, int radius = 4,
            CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(pixels);
            ArgumentNullException.ThrowIfNull(mask);
            if (width <= 0 || height <= 0 || (long)width * height != pixels.Length || mask.Length != pixels.Length)
                throw new ArgumentException("The image and inpainting mask dimensions must match.");
            if (radius is < 1 or > 32) throw new ArgumentOutOfRangeException(nameof(radius));
            var missing = 0;
            foreach (var coverage in mask)
                if (coverage != 0)
                    missing++;
            if (missing == 0) return;
            if (missing == pixels.Length)
                throw new InvalidOperationException("Inpainting requires known pixels outside the selected area.");
            new InpaintingFront(pixels, mask, width, height, radius, token).Restore(missing);
        }
    }
}

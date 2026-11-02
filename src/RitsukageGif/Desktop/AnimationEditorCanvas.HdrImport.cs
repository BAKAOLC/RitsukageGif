using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ImageMagick;

namespace RitsukageGif.Desktop
{
    internal sealed partial class AnimationEditorCanvas
    {
        private static unsafe WriteableBitmap CreateImportedBitmap(string path, CancellationToken token)
        {
            var first = ReadImportedPixels(path, token);
            var bitmap = new WriteableBitmap(new(first.Width, first.Height), new(96, 96),
                PixelFormat.Bgra8888, AlphaFormat.Unpremul);
            try
            {
                using var locked = bitmap.Lock();
                fixed (byte* source = first.Pixels)
                    for (var y = 0; y < first.Height; y++)
                    {
                        token.ThrowIfCancellationRequested();
                        Buffer.MemoryCopy(source + y * first.Width * 4, (byte*)locked.Address + y * locked.RowBytes,
                            locked.RowBytes, first.Width * 4);
                    }

                return bitmap;
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }
        }

        private static AnimationEditorFrameData ReadImportedPixels(string path, CancellationToken token)
        {
            if (PngAnimation.HasHdrPqMetadata(path, token))
            {
                var frame = PngAnimation.ReadToneMappedFrames(path, token).First().Frame;
                MemoryMarshal.TryGetArray(frame.Pixels, out var memory);
                return new(frame.Width, frame.Height, memory.Array!);
            }

            using var image = ImageCompositionService.ReadSdrWorkingImage(path, token);
            using var pixels = image.GetPixels();
            return new(checked((int)image.Width), checked((int)image.Height), pixels.ToByteArray(PixelMapping.BGRA)!);
        }
    }
}

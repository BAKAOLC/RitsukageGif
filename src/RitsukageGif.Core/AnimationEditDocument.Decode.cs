using System.Runtime.InteropServices;
using ImageMagick;
using SkiaSharp;

namespace RitsukageGif.Core
{
    // ReSharper disable once ClassCannotBeInstantiated
    public sealed partial class AnimationEditDocument
    {
        private static IEnumerable<IMagickImage<byte>> ReadAnimationFrames(string path, MediaInfo info,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using var stream = File.OpenRead(path);
            using var codec = SKCodec.Create(stream) ?? throw new InvalidDataException();
            if (codec.Info.Width != info.Width || codec.Info.Height != info.Height || codec.FrameCount != info.Frames)
                throw new InvalidDataException();
            using var colorSpace = SKColorSpace.CreateSrgb();
            var imageInfo = new SKImageInfo(info.Width, info.Height, SKColorType.Bgra8888,
                SKAlphaType.Premul, colorSpace);
            using var canvas = new SKBitmap();
            using var restore = new SKBitmap();
            using var straight = new SKBitmap();
            if (!canvas.TryAllocPixels(imageInfo) ||
                !straight.TryAllocPixels(imageInfo.WithAlphaType(SKAlphaType.Unpremul)))
                throw new OutOfMemoryException(UiText.Get("MemoryLimit"));
            canvas.Erase(SKColors.Transparent);
            var pixels = new byte[checked(info.Width * info.Height * 4)];
            var frameInfo = codec.FrameInfo;
            var prior = -1;
            for (var index = 0; index < info.Frames; index++)
            {
                token.ThrowIfCancellationRequested();
                var previous = prior;
                var restorePrevious = frameInfo[index].DisposalMethod == SKCodecAnimationDisposalMethod.RestorePrevious;
                if (restorePrevious) CopyFrame(canvas, restore);
                var result = codec.GetPixels(canvas.Info, canvas.GetPixels(), new(index, prior));
                if (result != SKCodecResult.Success)
                    throw new InvalidDataException($"Animation frame decoding failed: frame={index}, result={result}.");
                using (var source = canvas.PeekPixels())
                    if (!source.ReadPixels(straight.Info, straight.GetPixels(), straight.RowBytes, 0, 0))
                        throw new InvalidDataException();
                for (var y = 0; y < info.Height; y++)
                {
                    token.ThrowIfCancellationRequested();
                    Marshal.Copy(straight.GetPixels() + y * straight.RowBytes, pixels, y * info.Width * 4,
                        info.Width * 4);
                }

                using var image = new MagickImage(pixels, new PixelReadSettings((uint)info.Width, (uint)info.Height,
                    StorageType.Char, PixelMapping.BGRA));
                image.AnimationTicksPerSecond = 1000;
                image.AnimationDelay = (uint)Math.Max(1, frameInfo[index].Duration);
                image.AnimationIterations = codec.RepetitionCount < 0 ? 0 : (uint)codec.RepetitionCount + 1;
                yield return image;
                if (restorePrevious)
                {
                    CopyFrame(restore, canvas);
                    prior = previous;
                }
                else
                {
                    prior = index;
                }
            }
        }

        private static void CopyFrame(SKBitmap source, SKBitmap target)
        {
            if (target.Info != source.Info && !target.TryAllocPixels(source.Info))
                throw new OutOfMemoryException(UiText.Get("MemoryLimit"));
            using var pixels = source.PeekPixels();
            if (!pixels.ReadPixels(target.Info, target.GetPixels(), target.RowBytes, 0, 0))
                throw new InvalidDataException();
        }
    }
}

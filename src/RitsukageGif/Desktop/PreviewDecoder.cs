using System.Buffers;
using System.Runtime.InteropServices;
using ImageMagick;
using SkiaSharp;

namespace RitsukageGif.Desktop
{
    internal static class PreviewDecoder
    {
        public static IEnumerable<PreviewFrame> Read(string path, CancellationToken token)
        {
            using var codec = SKCodec.Create(path) ?? throw new InvalidDataException();
            var info = codec.Info.WithColorType(SKColorType.Bgra8888).WithAlphaType(SKAlphaType.Premul);
            using var canvas = new SKBitmap(info);
            using var restore = new SKBitmap();
            if (codec.EncodedFormat == SKEncodedImageFormat.Png)
            {
                using var surface = new SKCanvas(canvas);
                surface.Clear(SKColors.Transparent);
                using var bitmap = new SKBitmap();
                using var blend = new SKPaint();
                using var clear = new SKPaint();
                clear.BlendMode = SKBlendMode.Clear;
                foreach (var frame in PngAnimation.ReadFrames(path, token))
                    using (frame)
                    {
                        token.ThrowIfCancellationRequested();
                        if (frame.GifDisposeMethod == GifDisposeMethod.Previous) Copy(canvas, restore);
                        var frameInfo = new SKImageInfo((int)frame.Width, (int)frame.Height, SKColorType.Bgra8888,
                            SKAlphaType.Unpremul);
                        if (bitmap.Info != frameInfo && !bitmap.TryAllocPixels(frameInfo))
                            throw new OutOfMemoryException();
                        using var pixels = frame.GetPixels();
                        var bytes = pixels.ToByteArray(PixelMapping.BGRA)!;
                        Marshal.Copy(bytes, 0, bitmap.GetPixels(), bytes.Length);

                        blend.BlendMode = frame.Compose == CompositeOperator.Src
                            ? SKBlendMode.Src
                            : SKBlendMode.SrcOver;
                        surface.DrawBitmap(bitmap, frame.Page.X, frame.Page.Y, blend);
                        yield return Snapshot((int)(frame.AnimationDelay * 1000L /
                                                    Math.Max(1, frame.AnimationTicksPerSecond)));
                        if (frame.GifDisposeMethod == GifDisposeMethod.Previous)
                        {
                            Copy(restore, canvas);
                        }
                        else if (frame.GifDisposeMethod == GifDisposeMethod.Background)
                        {
                            surface.DrawRect(frame.Page.X, frame.Page.Y, frame.Width, frame.Height, clear);
                        }
                    }

                yield break;
            }

            var metadata = codec.FrameInfo;
            var prior = -1;
            for (var i = 0; i < Math.Max(1, codec.FrameCount); i++)
            {
                token.ThrowIfCancellationRequested();
                var previous = prior;
                var restorePrevious = metadata.Length > i &&
                                      metadata[i].DisposalMethod == SKCodecAnimationDisposalMethod.RestorePrevious;
                if (restorePrevious) Copy(canvas, restore);
                var result = codec.GetPixels(info, canvas.GetPixels(), new SKCodecOptions(i, prior));
                if (result != SKCodecResult.Success) throw new InvalidDataException(result.ToString());
                yield return Snapshot(metadata.Length > i ? metadata[i].Duration : 100);
                if (restorePrevious)
                {
                    Copy(restore, canvas);
                    prior = previous;
                }
                else
                {
                    prior = i;
                }
            }

            PreviewFrame Snapshot(int delay)
            {
                token.ThrowIfCancellationRequested();
                var length = checked(canvas.Width * canvas.Height * 4);
                var buffer = ArrayPool<byte>.Shared.Rent(length);
                try
                {
                    if (canvas.RowBytes == canvas.Width * 4)
                        Marshal.Copy(canvas.GetPixels(), buffer, 0, length);
                    else
                        for (var y = 0; y < canvas.Height; y++)
                            Marshal.Copy(canvas.GetPixels() + y * canvas.RowBytes, buffer, y * canvas.Width * 4,
                                canvas.Width * 4);
                    return new PreviewFrame(buffer, canvas.Width, canvas.Height, Math.Max(10, delay));
                }
                catch
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                    throw;
                }
            }
        }

        private static void Copy(SKBitmap source, SKBitmap target)
        {
            if (target.Info != source.Info && !target.TryAllocPixels(source.Info)) throw new OutOfMemoryException();
            using var canvas = new SKCanvas(target);
            using var paint = new SKPaint();
            paint.BlendMode = SKBlendMode.Src;
            canvas.DrawBitmap(source, 0, 0, paint);
        }
    }
}

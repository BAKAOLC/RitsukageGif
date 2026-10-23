using System.Runtime.CompilerServices;
using ImageMagick;

namespace RitsukageGif.Core
{
    public static class AnimationEncoder
    {
        public static async Task EncodeAsync(IReadOnlyList<StoredFrame> frames, string output, RecordingOptions options,
            IProgress<RecordingProgress>? progress, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (frames.Count == 0) throw new ArgumentException(UiText.Get("NoInputFrames"));
            if (options.UseFfmpeg && FfmpegEncoder.FindExecutable() is { } executable)
                await FfmpegEncoder.EncodeAsync(executable, frames, output, options, token);
            else if (options.Format == OutputFormat.Gif)
                await StreamingGifEncoder.EncodeAsync(ReadFrames(frames, token), output, options, progress, token);
            else
                await Task.Run(() => EncodeOther(frames, output, options, progress, token), token);
            progress?.Report(new RecordingProgress(RecordingStage.Complete, frames.Count, frames.Count));
        }

        private static async IAsyncEnumerable<RawFrame> ReadFrames(IReadOnlyList<StoredFrame> frames,
            [EnumeratorCancellation] CancellationToken token)
        {
            foreach (var frame in frames)
            {
                token.ThrowIfCancellationRequested();
                using var image = new MagickImage(await File.ReadAllBytesAsync(frame.Path, token));
                using var pixels = image.GetPixels();
                yield return new RawFrame(pixels.ToByteArray(PixelMapping.BGRA)!, frame.DelayMs);
            }
        }

        private static void EncodeOther(IReadOnlyList<StoredFrame> frames, string output, RecordingOptions options,
            IProgress<RecordingProgress>? progress, CancellationToken token)
        {
            if (options.Format == OutputFormat.APng)
            {
                PngAnimation.Encode(frames, output, token);
                return;
            }

            if ((long)options.Width * options.Height * 4 * frames.Count > 512L * 1024 * 1024)
                throw new InvalidOperationException(UiText.Get("MemoryLimit"));
            using var images = new MagickImageCollection();
            for (var i = 0; i < frames.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                var image = new MagickImage(frames[i].Path)
                {
                    AnimationTicksPerSecond = 1000,
                    AnimationDelay = (uint)frames[i].DelayMs,
                    AnimationIterations = 0
                };
                images.Add(image);
                progress?.Report(new RecordingProgress(RecordingStage.Encoding, frames.Count, i + 1));
            }

            images[0].Settings.SetDefine(MagickFormat.WebP, "lossless", true);
            images.Write(output, MagickFormat.WebP);
            token.ThrowIfCancellationRequested();
        }
    }
}

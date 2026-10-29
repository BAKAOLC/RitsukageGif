using System.Runtime.CompilerServices;
using ImageMagick;

namespace RitsukageGif.Core
{
    public static class AnimationEncoder
    {
        private static readonly ILogger Logger = LogHub.GetLogger("Core.Encoding");

        public static async Task EncodeAsync(IReadOnlyList<StoredFrame> frames, string output, RecordingOptions options,
            IProgress<RecordingProgress>? progress, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (frames.Count == 0) throw new ArgumentException(UiText.Get("NoInputFrames"));
            Logger.ZLogDebug(
                $"Stored animation encoding started: frames={frames.Count}, format={options.Format.ToString()}, externalEncoderRequested={options.UseFfmpeg}");
            if (options.UseFfmpeg && FfmpegEncoder.FindExecutable() is { } executable)
                await FfmpegEncoder.EncodeAsync(executable, frames, output, options, token);
            else
                await StreamingAnimationEncoder.EncodeAsync(ReadFrames(frames, token), output, options, progress,
                    token);
            progress?.Report(new(RecordingStage.Complete, frames.Count, frames.Count));
            Logger.ZLogDebug(
                $"Stored animation encoding completed: frames={frames.Count}, format={options.Format.ToString()}");
        }

        private static async IAsyncEnumerable<RawFrame> ReadFrames(IReadOnlyList<StoredFrame> frames,
            [EnumeratorCancellation] CancellationToken token)
        {
            foreach (var frame in frames)
            {
                token.ThrowIfCancellationRequested();
                using var image = new MagickImage(await File.ReadAllBytesAsync(frame.Path, token));
                using var pixels = image.GetPixels();
                yield return new(pixels.ToByteArray(PixelMapping.BGRA)!, frame.DelayMs);
            }
        }
    }
}

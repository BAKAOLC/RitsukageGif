namespace RitsukageGif.Core
{
    public static class StreamingAnimationEncoder
    {
        public static Task EncodeAsync(IAsyncEnumerable<RawFrame> frames, string output,
            RecordingOptions options, IProgress<RecordingProgress>? progress, CancellationToken token)
        {
            // Keep native codec initialization and synchronously available frames off the capture/UI thread.
            return Task.Run(() => options.Format switch
            {
                OutputFormat.Gif => StreamingGifEncoder.EncodeAsync(frames, output, options, progress, token),
                OutputFormat.WebP => StreamingWebpEncoder.EncodeAsync(frames, output, options, progress, token),
                OutputFormat.APng => StreamingPngEncoder.EncodeAsync(frames, output, options, progress, token),
                _ => throw new ArgumentOutOfRangeException(nameof(options))
            }, token);
        }
    }
}

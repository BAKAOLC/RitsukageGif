namespace RitsukageGif.Core
{
    public static class StreamingAnimationEncoder
    {
        private static readonly ILogger Logger = LogHub.GetLogger("Core.Encoding");

        public static Task EncodeAsync(IAsyncEnumerable<RawFrame> frames, string output,
            RecordingOptions options, IProgress<RecordingProgress>? progress, CancellationToken token)
        {
            // Keep native codec initialization and synchronously available frames off the capture/UI thread.
            return Task.Run(async () =>
            {
                Logger.ZLogDebug(
                    $"Streaming animation encoding started: format={options.Format.ToString()}, width={options.Width}, height={options.Height}, quality={options.Quality.ToString()}");
                try
                {
                    await (options.Format switch
                    {
                        OutputFormat.Gif => StreamingGifEncoder.EncodeAsync(frames, output, options, progress, token),
                        OutputFormat.WebP => StreamingWebpEncoder.EncodeAsync(frames, output, options, progress, token),
                        OutputFormat.APng => StreamingPngEncoder.EncodeAsync(frames, output, options, progress, token),
                        _ => throw new ArgumentOutOfRangeException(nameof(options)),
                    });
                    Logger.ZLogDebug($"Streaming animation encoding completed: format={options.Format.ToString()}");
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    Logger.ZLogDebug($"Streaming animation encoding canceled: format={options.Format.ToString()}");
                    throw;
                }
                catch (Exception error)
                {
                    Logger.ZLogError(error,
                        $"Streaming animation encoding failed: format={options.Format.ToString()}, exceptionType={error.GetType().Name}, hResult={error.HResult}");
                    throw;
                }
            }, token);
        }
    }
}

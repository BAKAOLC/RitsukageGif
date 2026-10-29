namespace RitsukageGif.Core
{
    public sealed partial class MediaProcessingService
    {
        private static readonly ILogger Logger = LogHub.GetLogger("Core.Media");

        private static MediaInfo ProcessWithDiagnostics(string input, string output, MediaProcessOptions options,
            CancellationToken token, PreparedMediaInput? prepared = null)
        {
            Logger.ZLogDebug(
                $"Media processing started: reuseDecodedInput={prepared is not null}, singleFrame={options.FrameIndex.HasValue}, sourceEffects={options.Effects?.Count ?? 0}");
            try
            {
                var result = Process(input, output, options, token, prepared);
                Logger.ZLogInformation(
                    $"Media processing completed: output={result.Path}, format={result.Format}, width={result.Width}, height={result.Height}, frames={result.Frames}, bytes={result.Bytes}");
                return result;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                Logger.ZLogDebug($"Media processing canceled");
                throw;
            }
            catch (Exception error)
            {
                Logger.ZLogError(error,
                    $"Media processing failed: output={output}, exceptionType={error.GetType().Name}, hResult={error.HResult}");
                throw;
            }
        }
    }
}

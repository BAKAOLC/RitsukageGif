using System.Diagnostics;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        private static readonly ILogger Logger = LogHub.GetLogger("Desktop.Annotation");
        private static long _sessionSequence;
        private readonly long _logSessionId = Interlocked.Increment(ref _sessionSequence);

        public ScreenshotAnnotationSession()
        {
            Logger.ZLogDebug($"Annotation session created. SessionId={_logSessionId}");
        }

        private async Task<T> LogCompositionAsync<T>(Task<T> operation, PixelRect region, int sourceDepth, bool hdr)
        {
            var started = Stopwatch.GetTimestamp();
            Logger.ZLogDebug(
                $"Annotation composition started. SessionId={_logSessionId} Width={region.Width} Height={region.Height} SourceDepth={sourceDepth} Hdr={hdr} Annotations={_annotations.Count}");
            try
            {
                var result = await operation;
                Logger.ZLogInformation(
                    $"Annotation composition completed. SessionId={_logSessionId} Width={region.Width} Height={region.Height} SourceDepth={sourceDepth} Hdr={hdr} ElapsedMs={Stopwatch.GetElapsedTime(started).TotalMilliseconds}");
                return result;
            }
            catch (OperationCanceledException)
            {
                Logger.ZLogDebug($"Annotation composition canceled. SessionId={_logSessionId} Hdr={hdr}");
                throw;
            }
            catch (Exception error)
            {
                Logger.ZLogError(error,
                    $"Annotation composition failed. SessionId={_logSessionId} Hdr={hdr} SourceDepth={sourceDepth}");
                throw;
            }
        }
    }
}

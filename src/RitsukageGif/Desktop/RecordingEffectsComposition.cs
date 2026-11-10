namespace RitsukageGif.Desktop
{
    internal static class RecordingEffectsComposition
    {
        internal static PreviewFrame Render(PreviewFrame source, TimeSpan position,
            RecordingInputTraceDocument? trace, RecordingInputEffects input, long frameIndex,
            RecordingReviewFrameRange range, double speed, int originalWidth, RecordingProgressEffects progress,
            int framesPerSecond, CancellationToken token)
        {
            using var buffer = new RecordingOverlayBuffer(source);
            if (trace is not null)
                RecordingInputOverlay.RenderInto(buffer, position, trace, input, framesPerSecond: framesPerSecond,
                    token: token);
            RecordingProgressOverlay.RenderInto(buffer, frameIndex, range, speed, originalWidth, progress, token);
            return buffer.TakeResult();
        }
    }
}

using Avalonia;

namespace RitsukageGif.Desktop

{
    internal abstract class RecordingReviewSource : IAsyncDisposable

    {
        internal abstract int Width { get; }

        internal abstract int Height { get; }

        internal abstract double Duration { get; }
        internal virtual bool IsHdr => false;
        internal virtual bool HasAudio => false;
        internal virtual int OriginalWidth => Width;
        internal virtual int OriginalHeight => Height;

        public abstract ValueTask DisposeAsync();

        internal virtual Task SetViewportAsync(PixelSize viewport, CancellationToken token)
        {
            return Task.CompletedTask;
        }

        internal abstract Task<PreviewFrame> FrameAtAsync(double position, CancellationToken token);

        internal static async Task<RecordingReviewSource> OpenAsync(string path, bool video, PixelSize viewport,
            CancellationToken token)

        {
            if (video) return await RecordingReviewVideoSource.CreateAsync(path, viewport, token);

            var info = await new MediaProcessingService().InspectAsync(path, token);

            return new RecordingReviewAnimationSource(path, info.Width, info.Height, info.DurationSeconds,
                PngAnimation.HasHdrPqMetadata(path, token), token);
        }
    }
}

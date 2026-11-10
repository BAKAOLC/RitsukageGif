namespace RitsukageGif.Contracts
{
    public interface ILongCaptureSession : IAsyncDisposable
    {
        Task StartAsync(CancellationToken token = default);
        Task PauseAsync(CancellationToken token = default);
        Task<ScrollCaptureResult> CaptureAsync(CancellationToken token = default);
        Task<ScrollCaptureResult> FeedAsync(ReadOnlyMemory<byte> encodedImage, CancellationToken token = default);
        Task<ScrollCaptureResult> FeedFramesAsync(CapturedFrameSet frames, CancellationToken token = default);
        Task MoveAsync(int position, CancellationToken token = default);
        Task<LongCaptureState> GetStateAsync(CancellationToken token = default);
        Task<ScrollCaptureImage> ReadAsync(CancellationToken token = default);
        Task<ScrollCaptureImage> StopAsync(CancellationToken token = default);
        Task<MediaInfo> SaveAsync(StaticCaptureOutput output, CancellationToken token = default);
    }
}

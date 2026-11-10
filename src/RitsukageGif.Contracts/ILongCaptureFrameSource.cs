namespace RitsukageGif.Contracts
{
    public interface ILongCaptureFrameSource : IAsyncDisposable
    {
        Task<CapturedFrameSet> CaptureAsync(PixelRect region, bool retainHdr, CancellationToken token = default);
        ValueTask SuspendAsync();
    }
}

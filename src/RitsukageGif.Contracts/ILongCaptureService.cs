namespace RitsukageGif.Contracts
{
    public interface ILongCaptureService : IAsyncDisposable
    {
        Task<ILongCaptureSession> CreateAsync(LongCaptureOptions options, CancellationToken token = default);
    }
}

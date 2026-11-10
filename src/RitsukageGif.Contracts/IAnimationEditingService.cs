namespace RitsukageGif.Contracts
{
    public interface IAnimationEditingService : IAsyncDisposable
    {
        Task<IAnimationDocument> OpenAsync(string path, CancellationToken token = default);

        Task<IAnimationDocument> CreateAsync(int width, int height, int delayMs = 100,
            CancellationToken token = default);
    }
}

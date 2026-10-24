namespace RitsukageGif.Contracts
{
    public interface IMediaOptimizationService
    {
        Task<MediaOptimizationResult> OptimizeAsync(string input, string output, MediaOptimizationOptions options,
            CancellationToken token = default);
    }
}

namespace RitsukageGif.Contracts
{
    public sealed record MediaOptimizationResult(
        bool Achieved,
        MediaInfo? Info,
        long SmallestBytes,
        int Attempts,
        MediaProcessOptions Settings);
}

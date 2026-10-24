namespace RitsukageGif.Contracts
{
    public sealed record MediaOptimizationOptions
    {
        public required long TargetBytes { get; init; }
        public MediaProcessOptions Processing { get; init; } = new();
        public int? MinimumQuality { get; init; }
        public int? MinimumColors { get; init; }
        public double MinimumScale { get; init; } = 1;
        public int MaximumAttempts { get; init; } = 8;
    }
}

namespace RitsukageGif.Contracts
{
    public sealed record ScrollCaptureOptions
    {
        public ScrollCaptureAxis Axis { get; init; }
        public int LeadingInset { get; init; }
        public int TrailingInset { get; init; }

        // ReSharper disable once AutoPropertyCanBeMadeGetOnly.Global
        public int MaximumLength { get; init; } = 32768;

        // ReSharper disable once AutoPropertyCanBeMadeGetOnly.Global
        public long MaximumBytes { get; init; } = 256L * 1024 * 1024;

        // ReSharper disable once AutoPropertyCanBeMadeGetOnly.Global
        public double MinimumOverlap { get; init; } = 0.3;
    }
}

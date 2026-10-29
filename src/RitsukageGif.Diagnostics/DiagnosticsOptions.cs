using Microsoft.Extensions.Logging;

namespace RitsukageGif.Diagnostics
{
    public sealed record DiagnosticsOptions
    {
        public LogLevel MinimumLevel { get; init; } = LogLevel.Debug;

        public IReadOnlyDictionary<string, LogLevel> CategoryLevels { get; init; } =
            new Dictionary<string, LogLevel>(StringComparer.Ordinal);

        // ReSharper disable once AutoPropertyCanBeMadeGetOnly.Global
        public int QueueCapacity { get; init; } = 8192;

        // ReSharper disable once AutoPropertyCanBeMadeGetOnly.Global
        public int ReservedErrorCapacity { get; init; } = 1024;

        // ReSharper disable once AutoPropertyCanBeMadeGetOnly.Global
        public int RollingSizeKiB { get; init; } = 8192;

        // ReSharper disable once AutoPropertyCanBeMadeGetOnly.Global
        public int MaximumFiles { get; init; } = 32;

        // ReSharper disable once AutoPropertyCanBeMadeGetOnly.Global
        public long MaximumTotalBytes { get; init; } = 256L * 1024 * 1024;

        // ReSharper disable once AutoPropertyCanBeMadeGetOnly.Global
        public TimeSpan MaximumAge { get; init; } = TimeSpan.FromDays(7);

        internal void Validate()
        {
            if (!Enum.IsDefined(MinimumLevel)) throw new ArgumentOutOfRangeException(nameof(MinimumLevel));
            ArgumentNullException.ThrowIfNull(CategoryLevels);
            foreach (var pair in CategoryLevels)
                if (string.IsNullOrWhiteSpace(pair.Key) || !Enum.IsDefined(pair.Value))
                    throw new ArgumentException("Category levels must contain valid names and levels.",
                        nameof(CategoryLevels));
            if (QueueCapacity < 16) throw new ArgumentOutOfRangeException(nameof(QueueCapacity));
            if (ReservedErrorCapacity < 1 || ReservedErrorCapacity >= QueueCapacity)
                throw new ArgumentOutOfRangeException(nameof(ReservedErrorCapacity));
            if (RollingSizeKiB < 1) throw new ArgumentOutOfRangeException(nameof(RollingSizeKiB));
            if (MaximumFiles < 1) throw new ArgumentOutOfRangeException(nameof(MaximumFiles));
            if (MaximumTotalBytes < 1024) throw new ArgumentOutOfRangeException(nameof(MaximumTotalBytes));
            if (MaximumAge <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(MaximumAge));
        }
    }
}

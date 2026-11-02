namespace RitsukageGif.Desktop
{
    internal sealed record CaptureRegionHistoryEntry(
        SelectionResult Selection,
        // ReSharper disable once NotAccessedPositionalProperty.Global
        DateTimeOffset Timestamp)
    {
        internal Guid Id { get; init; } = Guid.NewGuid();
    }
}

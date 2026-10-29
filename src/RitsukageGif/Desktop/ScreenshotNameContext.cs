namespace RitsukageGif.Desktop
{
    public sealed record ScreenshotNameContext(
        DateTime Timestamp,
        string? Title = null,
        string? Process = null,
        int Width = 0,
        int Height = 0)
    {
        public Guid Id { get; init; } = Guid.NewGuid();
    }
}

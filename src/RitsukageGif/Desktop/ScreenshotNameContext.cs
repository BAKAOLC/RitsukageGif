namespace RitsukageGif.Desktop
{
    public sealed record ScreenshotNameContext(
        DateTime Timestamp,
        string? Title = null,
        string? Process = null,
        int Width = 0,
        int Height = 0)
    {
        public Guid Id { get; } = Guid.NewGuid();
        internal Guid TimeOrderedId { get; } = Guid.CreateVersion7(new(Timestamp));
    }
}

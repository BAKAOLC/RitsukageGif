namespace RitsukageGif.Contracts
{
    public sealed record VideoCaptureHandle(
        string Id,
        string Path,
        bool Cancelled = false,
        IReadOnlyList<string>? Outputs = null);
}

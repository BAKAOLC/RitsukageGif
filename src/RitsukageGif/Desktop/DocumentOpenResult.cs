namespace RitsukageGif.Desktop
{
    internal sealed record DocumentOpenResult(string Path, bool Opened, string? Error = null);
}

namespace RitsukageGif.Core
{
    internal sealed record RetainedRecording(
        string Path,
        DateTime SavedUtc,
        long Length,
        DateTime LastWriteUtc,
        DateTime CreatedUtc);
}

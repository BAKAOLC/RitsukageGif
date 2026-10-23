namespace RitsukageGif.Core
{
    public sealed record MultiRecordingResult(
        string Name,
        string Path,
        int Captured,
        int Encoded,
        double ElapsedSeconds,
        string? Error)
    {
        public bool Success => Error is null;
    }
}

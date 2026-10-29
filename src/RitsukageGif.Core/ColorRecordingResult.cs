namespace RitsukageGif.Core
{
    public sealed record ColorRecordingResult(string? SdrPath, string HdrPath, int CapturedFrames, TimeSpan Duration);
}

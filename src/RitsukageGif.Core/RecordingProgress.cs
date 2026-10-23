namespace RitsukageGif.Core
{
    public sealed record RecordingProgress(
        RecordingStage Stage,
        int Captured,
        int Encoded,
        double CapturedFps = 0,
        double ElapsedSeconds = 0);
}

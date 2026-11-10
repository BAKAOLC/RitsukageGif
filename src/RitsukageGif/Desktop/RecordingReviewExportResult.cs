namespace RitsukageGif.Desktop
{
    internal sealed record RecordingReviewExportResult(
        IReadOnlyList<string> Outputs,
        bool ExitRecording,
        bool AutoExitAfterEncoding);
}

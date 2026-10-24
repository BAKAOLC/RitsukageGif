namespace RitsukageGif.Core
{
    internal sealed record RecordingRetentionIndex
    {
        public List<RetainedRecording> Files { get; init; } = [];
        public string[] ClipboardPaths { get; set; } = [];
    }
}

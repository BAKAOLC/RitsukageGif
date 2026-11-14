namespace RitsukageGif.Desktop
{
    internal sealed class ScreenshotInvocation(bool propagateErrors)
    {
        private readonly List<string> _paths = [];

        internal bool PropagateErrors { get; } = propagateErrors;
        internal bool DeferCopyOutput { get; init; }
        internal CancellationToken CommandToken { get; init; }
        internal bool RecordingPublished { get; private set; }
        internal IReadOnlyList<string> Paths => _paths;
        internal PixelRect? Region { get; set; }

        internal void PublishRecording(IReadOnlyList<string> paths, PixelRect region)
        {
            RecordingPublished = true;
            Region = region;
            AddPaths(paths);
        }

        internal void AddPaths(IEnumerable<string> paths)
        {
            _paths.AddRange(paths);
        }
    }
}

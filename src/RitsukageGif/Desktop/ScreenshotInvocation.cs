namespace RitsukageGif.Desktop
{
    internal sealed class ScreenshotInvocation(bool propagateErrors)
    {
        private readonly List<string> _paths = [];

        internal bool PropagateErrors { get; } = propagateErrors;
        internal IReadOnlyList<string> Paths => _paths;
        internal PixelRect? Region { get; set; }

        internal void AddPaths(IEnumerable<string> paths)
        {
            _paths.AddRange(paths);
        }
    }
}

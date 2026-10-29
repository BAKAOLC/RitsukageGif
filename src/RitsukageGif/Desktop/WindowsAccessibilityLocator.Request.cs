namespace RitsukageGif.Desktop
{
    internal static partial class WindowsAccessibilityLocator
    {
        private sealed record Request(
            WindowTarget Window,
            PixelPoint Point,
            long Started,
            CancellationToken CancellationToken)
        {
            public TaskCompletionSource<IReadOnlyList<WindowControlTarget>> Completion { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}

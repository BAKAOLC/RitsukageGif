namespace RitsukageGif.Desktop
{
    internal static partial class WindowsAccessibilityLocator
    {
        private sealed record FocusRequest(nint Foreground, uint Process, long Started, CancellationToken Token)
        {
            internal TaskCompletionSource<WindowsFocusedElementIdentity?> Completion { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}

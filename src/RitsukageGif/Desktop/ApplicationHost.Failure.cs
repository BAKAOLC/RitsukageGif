namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private bool _fatalFailure;

        internal void SuspendAfterFailure()
        {
            _fatalFailure = true;
            IsClosing = true;
            HostLogger.ZLogCritical(
                $"Application entering failure suspension. Recording={IsRecording} Selecting={IsSelecting} TakingScreenshot={IsTakingScreenshot}");
            RunShutdownStage("failure global hotkeys", () => _hotkeys?.Dispose());
            _hotkeys = null;
            RunShutdownStage("failure global mouse", () => _globalMouse?.Dispose());
            _globalMouse = null;
            RunShutdownStage("failure session shortcuts", StopSessionShortcuts);
            RunShutdownStage("failure recording stop", () => _stop?.Cancel());
            RunShutdownStage("failure recording abort", () => _abort?.Cancel());
            RunShutdownStage("failure selection cancellation", CancelSelection);
            RunShutdownStage("failure screenshot cancellation", CancelScreenshot);
        }
    }
}

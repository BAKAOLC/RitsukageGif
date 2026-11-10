using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private bool TaskKeepAlive { get; }

        private bool CanRestoreTaskPins => !IsClosing && IsTaskHost && TaskKeepAlive &&
                                           _commands is { IsRunning: true };

        private void TaskCommandServerStopped(Exception? error)
        {
            Dispatcher.UIThread.Post(() => _ = RecoverTaskCommandServerAsync(error));
        }

        private async Task RecoverTaskCommandServerAsync(Exception? error)
        {
            if (IsClosing || !IsTaskHost) return;
            if (error is null) HostLogger.ZLogError($"Task command server stopped unexpectedly.");
            else HostLogger.ZLogError(error, $"Task command server failed.");
            _fatalFailure = true;
            if (error is not null) CrashRecoverySession.Current?.RecordFailure(error);
            TaskExitCode = 1;
            try
            {
                await ShutdownAsync();
            }
            catch (Exception shutdownError)
            {
                HostLogger.ZLogError(shutdownError,
                    $"Task shutdown after command server failure did not complete.");
            }

            if (IsClosing) return;
            try
            {
                _pinnedImages.RestoreAutomationInteraction();
            }
            catch (Exception recoveryError)
            {
                HostLogger.ZLogError(recoveryError,
                    $"Task pin interaction recovery failed after command server shutdown was canceled.");
            }
        }
    }
}

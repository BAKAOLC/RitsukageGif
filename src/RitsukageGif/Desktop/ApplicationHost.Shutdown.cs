using System.Diagnostics;

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private void RunShutdownStage(string stage, Action action)
        {
            HostLogger.ZLogDebug($"Shutdown stage started. Stage={stage}");
            try
            {
                action();
                HostLogger.ZLogDebug($"Shutdown stage completed. Stage={stage}");
            }
            catch (Exception error)
            {
                ReportShutdownFailure(stage, error);
            }
        }

        private async Task RunShutdownStageAsync(string stage, Func<Task> action,
            CancellationToken ownedCancellation = default)
        {
            HostLogger.ZLogDebug($"Asynchronous shutdown stage started. Stage={stage}");
            try
            {
                await action();
                HostLogger.ZLogDebug($"Asynchronous shutdown stage completed. Stage={stage}");
            }
            catch (OperationCanceledException) when (ownedCancellation.IsCancellationRequested)
            {
                Trace.TraceInformation("Shutdown completed cancelled operation {0}.", stage);
            }
            catch (Exception error)
            {
                ReportShutdownFailure(stage, error);
            }
        }

        private void ReportShutdownFailure(string stage, Exception error)
        {
            _fatalFailure = true;
            HostLogger.ZLogError(error, $"Shutdown stage failed. Stage={stage}");
        }
    }
}

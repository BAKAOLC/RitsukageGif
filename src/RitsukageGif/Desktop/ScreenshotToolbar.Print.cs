using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotToolbar
    {
        private static readonly ILogger PrintCapabilityLogger = LogHub.GetLogger("Desktop.ToolbarPrint");
        private readonly CancellationTokenSource _printCapabilityLifetime = new();
        private bool _printCapabilityPending;

        private void InitializePrintCapabilities()
        {
            if (_actionButtons.TryGetValue(ToolbarActionId.Print, out var button))
                ToolTip.SetShowOnDisabled(button, true);
            _ = RefreshPrintCapabilitiesAsync();
        }

        private async Task RefreshPrintCapabilitiesAsync()
        {
            if (_disposed || _printCapabilityPending || !ScreenshotPrintService.IsSupported) return;
            _printCapabilityPending = true;
            using var work = CancellationTokenSource.CreateLinkedTokenSource(_printCapabilityLifetime.Token);
            work.CancelAfter(TimeSpan.FromSeconds(6));
            try
            {
                var capabilities = await ScreenshotPrintService.GetCapabilitiesAsync(work.Token);
                if (_disposed) return;
                if (!capabilities.Available)
                    PrintCapabilityLogger.ZLogDebug(
                        $"Native printing unavailable. Detail={capabilities.FailureReason}");
                if (_actionButtons.TryGetValue(ToolbarActionId.Print, out var button))
                {
                    button.IsEnabled = !_session.IsBusy && capabilities.Available;
                    ToolTip.SetShowOnDisabled(button, true);
                    SetShortcutTip(button, SelectionCommand.Print,
                        detailKey: capabilities.Available ? null : "PrintUnavailable");
                }
            }
            catch (OperationCanceledException) when (work.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                PrintCapabilityLogger.ZLogDebug(error, $"Native print availability query failed.");
            }
            finally
            {
                _printCapabilityPending = false;
            }
        }
    }
}

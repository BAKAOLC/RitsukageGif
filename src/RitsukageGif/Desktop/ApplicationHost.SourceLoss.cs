using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private WindowSourceLoss ObserveWindowSourceLoss()
        {
            return new(_stop!, () =>
            {
                HostLogger.ZLogWarning($"Manually selected window capture source lost; stopping capture.");
                Dispatcher.UIThread.Post(() =>
                {
                    if (IsClosing) return;
                    IsRecording = false;
                    SetStatusKey("WindowSourceLostTitle");
                    Notify();
                });
            });
        }

        private void ShowWindowSourceLost()
        {
            Region = null;
            _manualPreset = null;
            Volatile.Write(ref _captureRegion, null);
            SetStatusKey("WindowSourceLostTitle");
            Notify();
            new CaptureSourceLostWindow(async () =>
            {
                if (!IsClosing) await SelectSourceAsync(CaptureSourceKind.Window);
            }).Show();
        }
    }
}

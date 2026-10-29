using Avalonia.Controls;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    public sealed partial class MainWindow
    {
        private WindowState _trayRestoreState = WindowState.Normal;
        private bool _trayTransitionQueued;
        internal bool IsHiddenInTray { get; private set; }

        private void InitializeTrayBehavior()
        {
            PropertyChanged += (_, change) =>
            {
                if (change.Property != WindowStateProperty) return;
                if (WindowState != WindowState.Minimized)
                {
                    if (!IsHiddenInTray) _trayRestoreState = WindowState;
                    return;
                }

                if (Settings.MinimizeToTray && !_allowClose && !_closing && !IsHiddenInTray)
                    QueueHideToTray(true);
            };
        }

        private bool TryCloseToTray(WindowClosingEventArgs args)
        {
            if (args.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown ||
                !Settings.CloseToTray || !_host.CanHideMainWindowToTray) return false;
            args.Cancel = true;
            QueueHideToTray(false);
            return true;
        }

        private void QueueHideToTray(bool minimizedOnly)
        {
            if (_trayTransitionQueued) return;
            _trayTransitionQueued = true;
            Dispatcher.UIThread.Post(() =>
            {
                _trayTransitionQueued = false;
                if (_allowClose || _closing || !IsVisible ||
                    (minimizedOnly && WindowState != WindowState.Minimized)) return;
                _host.HideToTray();
            });
        }

        internal void HideForTray()
        {
            if (IsHiddenInTray) return;
            if (WindowState != WindowState.Minimized) _trayRestoreState = WindowState;
            IsHiddenInTray = true;
            try
            {
                ShowInTaskbar = false;
                Hide();
            }
            catch
            {
                IsHiddenInTray = false;
                ShowInTaskbar = true;
                throw;
            }
        }

        internal void RestoreFromTray()
        {
            if (!IsHiddenInTray) return;
            IsHiddenInTray = false;
            ShowInTaskbar = true;
            WindowState = _trayRestoreState;
        }
    }
}

using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private Window? _desktopToolbarAdjustment;
        private bool _restoreDesktopToolbarAdjustment;

        private async Task AdjustDesktopToolbarAsync()
        {
            if (_desktopToolbar is not { } owner) return;
            if (_desktopToolbarAdjustment is { } existing)
            {
                WindowActivation.Show(existing);
                return;
            }

            var dialog = new DesktopToolbarAdjustmentWindow(Settings.System.DesktopToolbar, updated =>
            {
                var preferences = Settings.System.DesktopToolbar with
                {
                    Primary = updated.Primary, More = updated.More,
                };
                UpdateSettings(Settings with { System = Settings.System with { DesktopToolbar = preferences } });
            });
            _desktopToolbarAdjustment = dialog;
            try
            {
                await dialog.ShowDialog(owner);
            }
            finally
            {
                _desktopToolbarAdjustment = null;
                _restoreDesktopToolbarAdjustment = false;
            }
        }

        private void HideDesktopToolbarAdjustment()
        {
            if (_desktopToolbarAdjustment?.IsVisible != true) return;
            _restoreDesktopToolbarAdjustment = true;
            _desktopToolbarAdjustment.Hide();
        }

        private void RestoreDesktopToolbarAdjustment()
        {
            if (!_restoreDesktopToolbarAdjustment || _desktopToolbarAdjustment is not { } dialog ||
                _desktopToolbar is not { } owner) return;
            _restoreDesktopToolbarAdjustment = false;
            var activate = dialog.ShowActivated;
            dialog.ShowActivated = false;
            try
            {
                dialog.Show(owner);
            }
            finally
            {
                dialog.ShowActivated = activate;
            }
        }
    }
}

using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class ScreenshotScrollingWindow
    {
        private readonly ToggleButton _automatic = new();
        private bool _updatingAutomatic;

        private void InitializeAutomatic(Panel panel, WindowTarget? target, bool respondShortcuts)
        {
            _session.ConfigureAutomaticInput(WindowsAutomaticScrollInput.Create(_region, target));
            SetCompactContent(_automatic, "ScrollCaptureAutomatic", "ScrollCaptureAutomatic", true);
            _automatic.IsEnabled = _session.AutomaticAvailable;
            switch (_automatic.IsEnabled)
            {
                case true when respondShortcuts: AddShortcutHint(_automatic, "automatic"); break;
                case false:
                    _automatic.Bind(ToolTip.TipProperty, LiveText.Resource("ScrollCaptureAutomaticUnavailable"));
                    break;
            }

            _automatic.IsCheckedChanged += (_, _) =>
            {
                if (_outputting)
                {
                    _updatingAutomatic = true;
                    try
                    {
                        _automatic.IsChecked = _session.AutomaticEnabled;
                    }
                    finally
                    {
                        _updatingAutomatic = false;
                    }

                    return;
                }

                if (!_updatingAutomatic) _session.AutomaticEnabled = _automatic.IsChecked == true;
            };
            _session.AutomaticChanged += AutomaticChanged;
            panel.Children.Add(_automatic);
        }

        private void AutomaticChanged(AutomaticScrollState _)
        {
            var generation = _session.Generation;
            Dispatcher.UIThread.Post(() =>
            {
                if (_closed || generation != _session.Generation) return;
                _updatingAutomatic = true;
                try
                {
                    _automatic.IsChecked = _session.AutomaticEnabled;
                }
                finally
                {
                    _updatingAutomatic = false;
                }

                RefreshStatus();
            });
        }

        private static string? AutomaticStatusKey(AutomaticScrollState state)
        {
            return state switch
            {
                AutomaticScrollState.Running => "ScrollCaptureAutomaticRunning",
                AutomaticScrollState.AtEnd => "ScrollCaptureAutomaticAtEnd",
                AutomaticScrollState.NoChange => "ScrollCaptureAutomaticNoChange",
                AutomaticScrollState.Unmatched => "ScrollCaptureAutomaticUnmatched",
                AutomaticScrollState.Unavailable => "ScrollCaptureAutomaticUnavailable",
                AutomaticScrollState.Failed => "ScrollCaptureAutomaticFailed",
                _ => null,
            };
        }
    }
}

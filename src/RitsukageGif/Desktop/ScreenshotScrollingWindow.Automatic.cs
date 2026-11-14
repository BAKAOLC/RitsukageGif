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
        private AutomaticScrollConnection? _automaticConnection;
        private readonly CancellationTokenSource _automaticLifetime = new();
        private WindowTarget? _automaticTarget;
        private bool _connectingAutomatic;

        private void InitializeAutomatic(Panel panel, WindowTarget? target, bool respondShortcuts)
        {
            _automaticTarget = target;
            _automatic.IsEnabled = false;
            _ = ProbeAutomaticAsync();
            Closed += (_, _) =>
            {
                _automaticLifetime.Cancel();
                _session.ConfigureAutomaticInput(null);
                _automaticConnection?.Dispose();
                _automaticConnection = null;
            };
            SetCompactContent(_automatic, "ScrollCaptureAutomatic", "ScrollCaptureAutomatic", true);
            if (respondShortcuts) AddShortcutHint(_automatic, "automatic");

            _automatic.IsCheckedChanged += async (_, _) =>
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

                if (_updatingAutomatic || _connectingAutomatic) return;
                if (_automatic.IsChecked != true)
                {
                    _session.AutomaticEnabled = false;
                    return;
                }

                _connectingAutomatic = true;
                _automatic.IsEnabled = false;
                try
                {
                    if (!_session.AutomaticAvailable)
                    {
                        var connection = await Task.Run(() => AutomaticScrollInputFactory.ConnectAsync(_region,
                                _automaticTarget, NativeCaptureSession.AuthorizePointerAsync, _automaticLifetime.Token),
                            _automaticLifetime.Token);
                        if (_closed)
                        {
                            connection.Dispose();
                            return;
                        }

                        ReplaceAutomaticConnection(connection);
                    }

                    if (_automaticConnection?.Input is { } input)
                    {
                        var region = _region;
                        var generation = _session.Generation;
                        var prepared = await Task.Run(() => input.Prepare(region, _automaticLifetime.Token),
                            _automaticLifetime.Token);
                        if (_closed) return;
                        if (prepared && generation == _session.Generation && region == _region &&
                            _resizePointer is null)
                        {
                            if (_session.Paused) StartCapture();
                            _session.AutomaticEnabled = true;
                        }
                        else
                        {
                            _session.AutomaticEnabled = false;
                            ToolTip.SetTip(_automatic, input.UnavailableReason);
                        }
                    }

                    _updatingAutomatic = true;
                    try
                    {
                        _automatic.IsChecked = _session.AutomaticEnabled;
                    }
                    finally
                    {
                        _updatingAutomatic = false;
                    }
                }
                catch (OperationCanceledException) when (_automaticLifetime.IsCancellationRequested)
                {
                }
                catch (Exception error) when
                    (error is IOException or NotSupportedException or InvalidOperationException)
                {
                    ToolTip.SetTip(_automatic, error.Message);
                    _updatingAutomatic = true;
                    try
                    {
                        _automatic.IsChecked = false;
                    }
                    finally
                    {
                        _updatingAutomatic = false;
                    }
                }
                finally
                {
                    _connectingAutomatic = false;
                    if (!_closed)
                        _automatic.IsEnabled = !_outputting &&
                                               (_session.AutomaticAvailable ||
                                                _automaticConnection?.CanRequestAccess == true);
                }
            };
            _session.AutomaticChanged += AutomaticChanged;
            panel.Children.Add(_automatic);
        }

        private async Task ProbeAutomaticAsync()
        {
            try
            {
                var connection = await Task.Run(() => AutomaticScrollInputFactory.Probe(_region, _automaticTarget,
                    NativeCaptureSession.CurrentPointerBinding()), _automaticLifetime.Token);
                if (_closed)
                {
                    connection.Dispose();
                    return;
                }

                ReplaceAutomaticConnection(connection);
            }
            catch (OperationCanceledException) when (_automaticLifetime.IsCancellationRequested)
            {
            }
        }

        private void ReplaceAutomaticConnection(AutomaticScrollConnection connection)
        {
            _session.ConfigureAutomaticInput(connection.Input);
            _automaticConnection?.Dispose();
            _automaticConnection = connection;
            _automatic.IsEnabled = connection.Input is not null || connection.CanRequestAccess;
            ToolTip.SetTip(_automatic, connection.Reason);
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

                ToolTip.SetTip(_automatic,
                    _automaticConnection?.Input?.UnavailableReason ?? _automaticConnection?.Reason);
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

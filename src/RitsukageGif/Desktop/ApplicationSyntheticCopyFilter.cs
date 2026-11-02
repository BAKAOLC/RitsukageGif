using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace RitsukageGif.Desktop
{
    internal sealed class ApplicationSyntheticCopyFilter : IDisposable
    {
        private static readonly ILogger Logger = LogHub.GetLogger("Desktop.Input");
        private readonly TimeProvider _clock;
        private readonly IDisposable? _keySubscription;
        private readonly IDisposable? _pointerSubscription;
        private long? _lastCommandPress;
        private long? _lastMouseRelease;

        internal ApplicationSyntheticCopyFilter(bool subscribe = true, TimeProvider? clock = null)
        {
            _clock = clock ?? TimeProvider.System;
            if (!subscribe) return;
            _pointerSubscription = InputElement.PointerReleasedEvent.AddClassHandler<TopLevel>(
                (_, _) => PointerReleased(), RoutingStrategies.Tunnel, true);
            _keySubscription = InputElement.KeyDownEvent.AddClassHandler<TopLevel>(
                (_, args) => KeyDown(args), RoutingStrategies.Tunnel);
        }

        internal bool Enabled
        {
            get;
            set
            {
                if (field == value) return;
                field = value;
                _lastMouseRelease = null;
                _lastCommandPress = null;
            }
        }

        public void Dispose()
        {
            Enabled = false;
            _pointerSubscription?.Dispose();
            _keySubscription?.Dispose();
        }

        private void PointerReleased()
        {
            if (!Enabled) return;
            _lastMouseRelease = _clock.GetTimestamp();
            _lastCommandPress = null;
        }

        private void KeyDown(KeyEventArgs key)
        {
            if (!Enabled || key.Handled) return;
            var commandModifiers = Application.Current?.PlatformSettings?.HotkeyConfiguration.CommandModifiers ??
                                   (OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control);
            if ((commandModifiers.HasFlag(KeyModifiers.Control) && key.Key is Key.LeftCtrl or Key.RightCtrl) ||
                (commandModifiers.HasFlag(KeyModifiers.Meta) && key.Key is Key.LWin or Key.RWin))
            {
                _lastCommandPress = _clock.GetTimestamp();
                return;
            }

            if (key.Key != Key.C || !key.KeyModifiers.HasFlag(commandModifiers) ||
                _lastMouseRelease is not { } release || _lastCommandPress is not { } command) return;
            var now = _clock.GetTimestamp();
            if (_clock.GetElapsedTime(release, now) <= TimeSpan.FromMilliseconds(300) &&
                _clock.GetElapsedTime(command, now) <= TimeSpan.FromMilliseconds(100))
            {
                key.Handled = true;
                _lastCommandPress = null;
                _lastMouseRelease = null;
                Logger.ZLogDebug($"Synthetic copy input suppressed after pointer release.");
            }
        }
    }
}

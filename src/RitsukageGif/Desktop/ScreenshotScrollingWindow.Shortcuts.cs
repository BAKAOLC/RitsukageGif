using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class ScreenshotScrollingWindow
    {
        private readonly HashSet<Key> _shortcutKeys = [];
        private IReadOnlyList<BuiltinShortcutBinding> _longshotBindings = [];
        private bool _respondShortcuts;
        private SessionShortcutScope? _shortcutScope;

        private void InitializeShortcutHandling(bool respondShortcuts)
        {
            _respondShortcuts = respondShortcuts;
            _longshotBindings = BuiltinShortcuts.Snapshot();
            // ReSharper disable once AsyncVoidLambda
            AddHandler(KeyDownEvent, async (_, args) =>
            {
                if (_closed || _completed.Task.IsCompleted || _direction.IsDropDownOpen) return;
                if (args.Source is Visual source &&
                    (!ReferenceEquals(GetTopLevel(source), this) || ReferenceEquals(source, _direction) ||
                     _direction.IsVisualAncestorOf(source))) return;
                var action = BuiltinShortcuts.Resolve(_longshotBindings, "BuiltinShortcutLongshotContext",
                    args);
                if (action is null || (!_respondShortcuts && action != "cancel")) return;
                if (_outputting && action != "cancel") return;
                args.Handled = true;
                if (_shortcutKeys.Add(args.Key)) await ExecuteShortcut(action);
            }, RoutingStrategies.Tunnel);
            AddHandler(KeyUpEvent, (_, args) => _shortcutKeys.Remove(args.Key), RoutingStrategies.Tunnel, true);
            PropertyChanged += ShortcutActivationChanged;
            _direction.PropertyChanged += ShortcutPopupChanged;
            Deactivated += ShortcutsDeactivated;
        }

        private void EnsureShortcutScope()
        {
            if (_shortcutScope is not null) return;
            var bindings = new List<(string Shortcut, Func<Task> Action)>();
            foreach (var (contextKey, action, gesture) in _longshotBindings)
            {
                if (contextKey != "BuiltinShortcutLongshotContext" ||
                    (!_respondShortcuts && action != "cancel")) continue;
                bindings.Add((gesture.ToString(), () =>
                {
                    if (_shortcutScope is { NativeEnabled: true } && !_closed && !_completed.Task.IsCompleted)
                        return ExecuteShortcut(action);
                    return Task.CompletedTask;
                }));
            }

            _shortcutScope = new(bindings,
                !_outputting && !IsActive && !_direction.IsDropDownOpen);
        }

        private void ShortcutActivationChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (args.Property == IsActiveProperty) RefreshShortcutNativeState();
        }

        private void ShortcutPopupChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (args.Property == ComboBox.IsDropDownOpenProperty)
                RefreshShortcutNativeState();
        }

        private void RefreshShortcutNativeState()
        {
            if (_shortcutScope is { } scope)
                scope.NativeEnabled = !_outputting && !IsActive && !_direction.IsDropDownOpen;
        }

        private void ShortcutsDeactivated(object? sender, EventArgs args)
        {
            _shortcutKeys.Clear();
        }

        private void DisposeShortcutScope()
        {
            PropertyChanged -= ShortcutActivationChanged;
            _direction.PropertyChanged -= ShortcutPopupChanged;
            Deactivated -= ShortcutsDeactivated;
            _shortcutKeys.Clear();
            var scope = _shortcutScope;
            _shortcutScope = null;
            scope?.Dispose();
        }

        private async Task ExecuteShortcut(string action)
        {
            if (_outputting && action != "cancel") return;
            switch (action)
            {
                case "cancel": Close(); break;
                case "pause" when _pause.IsEnabled:
                    _pause.RaiseEvent(new(Button.ClickEvent));
                    break;
                case "automatic" when _automatic.IsEnabled:
                    _automatic.IsChecked = _automatic.IsChecked != true;
                    break;
                case "toggleaxis" when _direction.IsEnabled:
                    _direction.SelectedIndex = _direction.SelectedIndex == 0 ? 1 : 0;
                    break;
                case "finish" when _finish.IsEnabled:
                    await CompleteCapture(ScrollingCaptureAction.Copy);
                    break;
                case "edit" when _finish.IsEnabled:
                    await CompleteCapture(ScrollingCaptureAction.Edit);
                    break;
                case "crop-leading" when _cropLeading.IsEnabled: Crop(true); break;
                case "crop-trailing" when _cropTrailing.IsEnabled: Crop(false); break;
            }
        }
    }
}

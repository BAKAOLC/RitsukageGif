using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class ScreenshotScrollingWindow
    {
        private readonly List<Action> _shortcutHintRefreshers = [];
        private readonly HashSet<Key> _shortcutKeys = [];
        private IReadOnlyList<(string Action, KeyGesture Gesture)> _longshotBindings = [];
        private bool _respondShortcuts;
        private SessionShortcutScope? _shortcutScope;

        private void InitializeShortcutHandling(bool respondShortcuts)
        {
            _respondShortcuts = respondShortcuts;
            RefreshShortcutBindings();
            SelectionCommands.BindingsChanged += ShortcutBindingsChanged;
            // ReSharper disable once AsyncVoidLambda
            AddHandler(KeyDownEvent, async (_, args) =>
            {
                if (_closed || _completed.Task.IsCompleted || HasCaptureDialog ||
                    _direction.IsDropDownOpen) return;
                if (args.Source is Visual source &&
                    (!ReferenceEquals(GetTopLevel(source), this) || ReferenceEquals(source, _direction) ||
                     _direction.IsVisualAncestorOf(source))) return;
                if (!_respondShortcuts || args.Handled || BuiltinShortcutTextInput.OwnsKey(args)) return;
                string? action = null;
                foreach (var binding in _longshotBindings)
                    if (binding.Gesture.KeyModifiers == args.KeyModifiers &&
                        BuiltinKeys.Matches(binding.Gesture.Key, args.Key))
                    {
                        action = binding.Action;
                        break;
                    }

                if (action is null) return;
                if (_outputting && action is not ("cancel" or "close-alternate")) return;
                args.Handled = true;
                if (_shortcutKeys.Add(args.Key)) await ExecuteShortcut(action);
            }, RoutingStrategies.Tunnel);
            AddHandler(KeyUpEvent, (_, args) => _shortcutKeys.Remove(args.Key), RoutingStrategies.Tunnel, true);
            PropertyChanged += ShortcutActivationChanged;
            _direction.PropertyChanged += ShortcutPopupChanged;
            Deactivated += ShortcutsDeactivated;
        }

        private void ShortcutBindingsChanged()
        {
            if (_closed || _completed.Task.IsCompleted) return;
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(ShortcutBindingsChanged);
                return;
            }

            RefreshShortcutBindings();
            _shortcutKeys.Clear();
            foreach (var refresh in _shortcutHintRefreshers) refresh();
            if (_shortcutScope is not { } previous) return;
            _shortcutScope = null;
            previous.Dispose();
            EnsureShortcutScope();
        }

        private void RefreshShortcutBindings()
        {
            var catalog = BuiltinShortcuts.Snapshot();
            var bindings = new List<(string Action, KeyGesture Gesture)>();
            foreach (var binding in catalog)
                if (binding.ContextKey == "BuiltinShortcutLongshotContext")
                    bindings.Add((binding.Action, binding.Gesture));
            foreach (var binding in SelectionCommands.SnapshotBindings())
            {
                // ReSharper disable once SwitchExpressionHandlesSomeKnownEnumValuesWithExceptionInDefault
                var action = binding.Command switch
                {
                    SelectionCommand.Confirm => "confirm-copy",
                    SelectionCommand.Copy => "copy",
                    SelectionCommand.Save => "save",
                    SelectionCommand.QuickSave => "quick-save",
                    SelectionCommand.Pin => "pin",
                    SelectionCommand.RecognizeTextAndCopy => "ocr-copy",
                    _ => null,
                };
                if (action is not null) bindings.Add((action, new(binding.Key, binding.Modifiers)));
            }

            foreach (var binding in catalog)
                switch (binding.DefinitionId)
                {
                    case "builtin.selection.cancel.0": bindings.Add(("cancel", binding.Gesture)); break;
                    case "builtin.selection.cancel.1": bindings.Add(("close-alternate", binding.Gesture)); break;
                }

            _longshotBindings = bindings;
        }

        private void EnsureShortcutScope()
        {
            if (_shortcutScope is not null || !_respondShortcuts) return;
            var bindings = new List<(string Shortcut, Func<Task> Action)>();
            foreach (var (action, gesture) in _longshotBindings)
                bindings.Add((gesture.ToString(), () =>
                {
                    if (_shortcutScope is { NativeEnabled: true } && !_closed && !_completed.Task.IsCompleted)
                        return ExecuteShortcut(action);
                    return Task.CompletedTask;
                }));

            _shortcutScope = new(bindings,
                !_outputting && !HasCaptureDialog && !IsActive && !_direction.IsDropDownOpen, true);
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
                scope.NativeEnabled = !_outputting && !HasCaptureDialog && !IsActive && !_direction.IsDropDownOpen;
        }

        private void ShortcutsDeactivated(object? sender, EventArgs args)
        {
            _shortcutKeys.Clear();
        }

        private void DisposeShortcutScope()
        {
            SelectionCommands.BindingsChanged -= ShortcutBindingsChanged;
            _shortcutHintRefreshers.Clear();
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
            if (HasCaptureDialog || (_outputting && action is not ("cancel" or "close-alternate"))) return;
            switch (action)
            {
                case "cancel":
                case "close-alternate": await CloseByUserAsync(); break;
                case "start-stop" when _startStop.IsEnabled:
                    await ToggleCaptureAsync();
                    break;
                case "automatic" when _automatic.IsEnabled:
                    _automatic.IsChecked = _automatic.IsChecked != true;
                    break;
                case "toggleaxis" when _direction.IsEnabled:
                    _direction.SelectedIndex = _direction.SelectedIndex == 0 ? 1 : 0;
                    break;
                case "copy":
                case "confirm-copy": await CompleteCapture(ScrollingCaptureAction.Copy); break;
                case "save": await CompleteCapture(ScrollingCaptureAction.Save); break;
                case "quick-save": await CompleteCapture(ScrollingCaptureAction.QuickSave); break;
                case "pin": await CompleteCapture(ScrollingCaptureAction.Pin); break;
                case "ocr-copy": await CompleteCapture(ScrollingCaptureAction.OcrCopy); break;
                case "edit":
                    await CompleteCapture(ScrollingCaptureAction.Edit);
                    break;
                case "crop-leading" when _cropLeading.IsEnabled: Crop(true); break;
                case "crop-trailing" when _cropTrailing.IsEnabled: Crop(false); break;
            }
        }
    }
}

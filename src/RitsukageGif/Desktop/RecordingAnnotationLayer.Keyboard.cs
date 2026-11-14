using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace RitsukageGif.Desktop
{
    internal sealed partial class RecordingAnnotationLayer
    {
        private readonly HashSet<Key> _heldKeys = [];
        private readonly HashSet<Key> _handledKeys = [];
        private readonly HashSet<PopupRoot> _keyboardPopups = [];
        private Window? _keyboardOwner;
        private IDisposable? _keyboardPopupSubscription;
        private IReadOnlyList<SelectionCommandDefinition> _shortcutBindings = [];

        internal event Action? CancelRequested;

        private void InitializeKeyboard()
        {
            RefreshShortcuts();
            Strip.AttachedToVisualTree += (_, _) => AttachKeyboardOwner(TopLevel.GetTopLevel(Strip) as Window);
            Strip.DetachedFromVisualTree += (_, _) => AttachKeyboardOwner(null);
            _overlay.KeyPressed += HandleOwnedKeyDown;
            _overlay.KeyReleased += HandleOwnedKeyUp;
            _overlay.FocusLost += ReleaseKeys;
            _keyboardPopupSubscription = Visual.IsVisibleProperty.Changed.AddClassHandler<PopupRoot>((popup, _) =>
            {
                var owner = popup.ParentTopLevel;
                while (owner is PopupRoot parent) owner = parent.ParentTopLevel;
                if (popup.IsVisible &&
                    (owner == _keyboardOwner || _overlay.Owns(owner) ||
                     (owner is Window window && _hostedOwners.Contains(window))))
                {
                    if (!_keyboardPopups.Add(popup)) return;
                    popup.AddHandler(InputElement.KeyDownEvent, HandleOwnedKeyDown, RoutingStrategies.Tunnel);
                    popup.AddHandler(InputElement.KeyUpEvent, HandleOwnedKeyUp, RoutingStrategies.Tunnel);
                }
                else if (_keyboardPopups.Remove(popup))
                {
                    popup.RemoveHandler(InputElement.KeyDownEvent, HandleOwnedKeyDown);
                    popup.RemoveHandler(InputElement.KeyUpEvent, HandleOwnedKeyUp);
                }
            });
        }

        private void AttachKeyboardOwner(Window? owner)
        {
            if (_keyboardOwner == owner) return;
            if (_keyboardOwner is { } previous)
            {
                previous.RemoveHandler(InputElement.KeyDownEvent, HandleOwnedKeyDown);
                previous.RemoveHandler(InputElement.KeyUpEvent, HandleOwnedKeyUp);
                previous.Deactivated -= KeyboardOwnerDeactivated;
            }

            _keyboardOwner = owner;
            ReleaseKeys();
            if (owner is null || _disposed) return;
            owner.AddHandler(InputElement.KeyDownEvent, HandleOwnedKeyDown, RoutingStrategies.Tunnel);
            owner.AddHandler(InputElement.KeyUpEvent, HandleOwnedKeyUp, RoutingStrategies.Tunnel);
            owner.Deactivated += KeyboardOwnerDeactivated;
        }

        private void KeyboardOwnerDeactivated(object? sender, EventArgs args)
        {
            ReleaseKeys();
        }

        private void ReleaseKeys()
        {
            Session.ReleaseNumberCaptionKey();
            Session.ReleasePolylineArrowKey();
            _heldKeys.Clear();
            _handledKeys.Clear();
        }

        private void HandleOwnedKeyDown(KeyEventArgs args)
        {
            HandleOwnedKeyDown(null, args);
        }

        private void HandleOwnedKeyDown(object? sender, KeyEventArgs args)
        {
            if (_tools.HandleMenuKey(args, true)) return;
            if (_disposed || args.Handled || Session.IsEditingText || IsTextInput(args.Source)) return;
            if (Session.HandleNumberCaptionKey(args.Key, args.KeyModifiers, true))
            {
                args.Handled = true;
                return;
            }

            if (!SelectionCommands.TryResolve(args.Key, args.KeyModifiers, out var definition, _shortcutBindings,
                    Session) ||
                definition is null) return;
            if (!_heldKeys.Add(args.Key) && !definition.Repeat)
            {
                args.Handled = _handledKeys.Contains(args.Key);
                return;
            }

            if (definition.Command == SelectionCommand.TogglePolylineArrows)
            {
                args.Handled = Session.HandlePolylineArrowKey(args.Key);
                if (args.Handled) _handledKeys.Add(args.Key);
                return;
            }

            args.Handled = ExecuteAnnotationCommand(definition);
            if (args.Handled) _handledKeys.Add(args.Key);
        }

        private void HandleOwnedKeyUp(KeyEventArgs args)
        {
            HandleOwnedKeyUp(null, args);
        }

        private void HandleOwnedKeyUp(object? sender, KeyEventArgs args)
        {
            if (_tools.HandleMenuKey(args, false)) return;
            if (!_disposed && Session.HandleNumberCaptionKey(args.Key, args.KeyModifiers, false)) args.Handled = true;
            Session.ReleasePolylineArrowKey(args.Key);
            _heldKeys.Remove(args.Key);
            _handledKeys.Remove(args.Key);
        }

        private static bool IsTextInput(object? source)
        {
            if (source is not Visual visual) return false;
            if (visual is TextBox || visual.GetVisualAncestors().OfType<TextBox>().Any()) return true;
            return source is Control control &&
                   TopLevel.GetTopLevel(control)?.FocusManager.GetFocusedElement() is TextBox;
        }

        private bool ExecuteAnnotationCommand(SelectionCommandDefinition definition)
        {
            // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
            switch (definition.Command)
            {
                case SelectionCommand.SelectTool when definition.Tool is { } tool:
                    Session.SelectDrawingTool(tool);
                    return true;
                case SelectionCommand.Undo:
                    Session.Undo();
                    return true;
                case SelectionCommand.Redo:
                    Session.Redo();
                    return true;
                case SelectionCommand.Confirm:
                    return Session.FinishRecordingDrawing();
                case SelectionCommand.FinishPolyline:
                    Session.FinishPolyline();
                    return true;
                case SelectionCommand.Cancel:
                    if (Session.IsBusy || Session.IsDrawing)
                    {
                        Session.CancelWork();
                    }
                    else if (Session.HasSelectedAnnotation)
                    {
                        Session.ClearAnnotationSelection();
                    }
                    else if (Session.Tool != ScreenshotTool.Select)
                    {
                        Session.Tool = ScreenshotTool.Select;
                    }
                    else
                    {
                        if (CancelRequested is null) return false;
                        CancelRequested.Invoke();
                    }

                    return true;
                case SelectionCommand.Clear:
                    if (Session.IsDrawing) Session.CancelGesture();
                    else if (Session.HasSelectedAnnotation) Session.DeleteSelectedAnnotation();
                    else return false;
                    return true;
                case SelectionCommand.ToggleAnnotations:
                    Session.ToggleAnnotations();
                    return true;
                case SelectionCommand.ToggleSecondaryEditing:
                    Session.ToggleSecondaryEditing();
                    return true;
                case SelectionCommand.ToggleAnnotationToolbar:
                    Session.ToggleToolbar();
                    return true;
                case SelectionCommand.ClearAnnotations:
                    Session.ClearAnnotations();
                    return true;
                case SelectionCommand.RenumberAnnotations:
                    Session.RenumberAnnotations();
                    return true;
                case SelectionCommand.Move when Session.HasSelectedAnnotation:
                    var dx = definition.DirectionKey switch { Key.Left => -1, Key.Right => 1, _ => 0 };
                    var dy = definition.DirectionKey switch { Key.Up => -1, Key.Down => 1, _ => 0 };
                    _ = Session.MoveSelectedAnnotationAsync(dx, dy);
                    return true;
                default:
                    return false;
            }
        }

        private void DisposeKeyboard()
        {
            AttachKeyboardOwner(null);
            foreach (var owner in _hostedOwners.ToArray()) DetachHostedSurface(owner);
            _overlay.KeyPressed -= HandleOwnedKeyDown;
            _overlay.KeyReleased -= HandleOwnedKeyUp;
            _overlay.FocusLost -= ReleaseKeys;
            _keyboardPopupSubscription?.Dispose();
            foreach (var popup in _keyboardPopups)
            {
                popup.RemoveHandler(InputElement.KeyDownEvent, HandleOwnedKeyDown);
                popup.RemoveHandler(InputElement.KeyUpEvent, HandleOwnedKeyUp);
            }

            _keyboardPopups.Clear();
        }
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedDocumentWindow
    {
        private static readonly Cursor HorizontalResize = new(StandardCursorType.SizeWestEast);
        private static readonly Cursor VerticalResize = new(StandardCursorType.SizeNorthSouth);
        private static readonly Cursor DiagonalResize = new(StandardCursorType.TopLeftCorner);
        private static readonly Cursor OtherDiagonalResize = new(StandardCursorType.TopRightCorner);
        private readonly MouseGestureTracker _gesture = new();
        private readonly MouseClickDispatcher _mouseClicks = new();
        private string? _dragFile;
        private PointerPressedEventArgs? _dragTrigger;
        private Avalonia.PixelPoint _dragPoint;
        private bool _dragging;
        private bool _nativeItemInteraction;

        private void ConfigureInput(ContextMenu menu)
        {
            _content.AddHandler(PointerPressedEvent, (_, args) =>
            {
                if (IsMarkEditing) return;
                if (BeginDocumentThumbnailPointer(args)) return;
                if (args.Source is Visual native && (native is ScrollBar ||
                                                     native.GetVisualAncestors().OfType<ScrollBar>().Any())) return;
                var properties = args.GetCurrentPoint(_content).Properties;
                var button = properties.PointerUpdateKind switch
                {
                    PointerUpdateKind.LeftButtonPressed => MouseButton.Left,
                    PointerUpdateKind.RightButtonPressed => MouseButton.Right,
                    PointerUpdateKind.MiddleButtonPressed => MouseButton.Middle,
                    PointerUpdateKind.XButton1Pressed => MouseButton.XButton1,
                    PointerUpdateKind.XButton2Pressed => MouseButton.XButton2,
                    _ => MouseButton.None,
                };
                var copyPlain = button != MouseButton.None && Document.PlainText.Length > 0 &&
                                HasPlainTextCopy(button, args.KeyModifiers, MouseCommands.SnapshotBindings());
                var itemButton = args.Source as Button ?? (args.Source as Visual)?.GetVisualAncestors()
                    .OfType<Button>().FirstOrDefault();
                _nativeItemInteraction = Document is PinnedFilesDocument && itemButton is not null &&
                                         !args.KeyModifiers.HasFlag(KeyModifiers.Alt) && !copyPlain;
                _dragFile = _nativeItemInteraction ? itemButton?.Tag as string : null;
                // ReSharper disable once ConvertIfStatementToSwitchStatement
                if (properties.IsLeftButtonPressed && ResizeEdge(args.GetPosition(this)) is { } edge)
                {
                    BeginResizeDrag(edge, args);
                    args.Handled = true;
                    return;
                }

                if (properties.IsLeftButtonPressed &&
                    ((Selectable && (!_isThumbnail || PreferencesProvider().ThumbnailSelectableText)) ||
                     (_textView is PinnedRichView && args.KeyModifiers.HasFlag(KeyModifiers.Control))) &&
                    _textView?.Control.IsPointerOver == true &&
                    !args.KeyModifiers.HasFlag(KeyModifiers.Alt) && !copyPlain) return;
                _mouseClicks.Press(button, args.KeyModifiers, args.ClickCount);
                if (_gesture.Begin(button, args.KeyModifiers, args.GetPosition(this), args.ClickCount))
                {
                    _dragTrigger = args;
                    _dragPoint = this.PointToScreen(args.GetPosition(this));
                    if (!_nativeItemInteraction) args.Pointer.Capture(_content);
                    if (args.KeyModifiers.HasFlag(KeyModifiers.Alt) || copyPlain) args.Handled = true;
                }
            }, RoutingStrategies.Tunnel);
            _content.PointerMoved += (sender, args) =>
            {
                if (IsMarkEditing) return;
                if (MoveDocumentThumbnailPointer(args)) return;
                if (!_gesture.IsActive)
                {
                    _content.Cursor = ResizeEdge(args.GetPosition(this)) switch
                    {
                        WindowEdge.West or WindowEdge.East => HorizontalResize,
                        WindowEdge.North or WindowEdge.South => VerticalResize,
                        WindowEdge.NorthWest or WindowEdge.SouthEast => DiagonalResize,
                        WindowEdge.NorthEast or WindowEdge.SouthWest => OtherDiagonalResize,
                        _ => null,
                    };
                    return;
                }

                if (_gesture.Move(args.GetPosition(this), 4, out var gesture))
                {
                    if (!MouseCommands.TryResolve(MouseGestureContext.Pinned, gesture, MouseCommands.SnapshotBindings(),
                            out var binding)) return;
                    if (StartDocumentThumbnailDrag(binding!.Action,
                            _dragTrigger!.GetPosition(_content)))
                    {
                        _gesture.Reset();
                        args.Pointer.Capture(_content);
                        MoveDocumentThumbnailPointer(args);
                        return;
                    }

                    _dragging = binding.Action is MouseAction.MoveWindow or MouseAction.MoveWindowIgnoringText;
                    if (binding.Action == MouseAction.DragContent && Document is PinnedFilesDocument files &&
                        _dragTrigger is { } trigger)
                    {
                        _gesture.Reset();
                        _ = RunAsync(() => DragFilesAsync(trigger, _dragFile is { } path ? [path] : files.Paths));
                        args.Handled = true;
                        return;
                    }

                    if (_dragging) args.Pointer.Capture(_content);
                }

                if (!_dragging || IsInteractionLocked) return;
                var point = this.PointToScreen(args.GetPosition(this));
                var delta = point - _dragPoint;
                _dragPoint = point;
                Position += delta;
                MoveSelectionRequested?.Invoke(this, new(delta.X, delta.Y));
                args.Handled = true;
            };
            _content.AddHandler(PointerReleasedEvent, (_, args) =>
            {
                if (FinishDocumentThumbnailPointer(args)) return;
                if (args.Route == RoutingStrategies.Tunnel == _nativeItemInteraction) return;
                if (!_gesture.IsActive) return;
                var click = _gesture.End(args.InitialPressMouseButton, out var gesture);
                args.Pointer.Capture(null);
                _dragging = false;
                _dragTrigger = null;
                var bindings = MouseCommands.SnapshotBindings();
                if (click && !_nativeItemInteraction && Document.PlainText.Length > 0 &&
                    HasPlainTextCopy(args.InitialPressMouseButton, args.KeyModifiers, bindings)) args.Handled = true;
                if (click && !_nativeItemInteraction &&
                    MouseCommands.TryResolve(MouseGestureContext.Pinned, gesture, bindings, out var binding))
                {
                    _mouseClicks.Dispatch(binding!, bindings, () => ExecuteMouse(gesture, menu, 1),
                        Application.Current?.PlatformSettings?.GetDoubleTapTime(PointerType.Mouse) ??
                        TimeSpan.FromMilliseconds(500),
                        () => !_closed && !_gesture.IsActive);
                    args.Handled = true;
                }
            }, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
            _content.PointerCaptureLost += (_, args) =>
            {
                if (!ReferenceEquals(args.Source, _content)) return;
                CancelDocumentThumbnailPointer();
                _gesture.Reset();
                _dragging = false;
                _dragTrigger = null;
            };
            _content.AddHandler(PointerWheelChangedEvent, (_, args) =>
            {
                if (IsMarkEditing || Math.Abs(args.Delta.Y) < double.Epsilon) return;
                args.Handled = ExecuteMouse(new(MouseGestureKind.Wheel, args.KeyModifiers), menu, args.Delta.Y);
            }, RoutingStrategies.Tunnel);
            AddHandler(KeyDownEvent, (sender, args) =>
            {
                if (args.Handled) return;
                if (IsMarkEditing)
                {
                    DocumentAnnotationKeyDown(args);
                    return;
                }

                var action = Document is PinnedTextDocument or PinnedHtmlDocument
                    ? BuiltinShortcuts.Resolve(BuiltinShortcuts.Snapshot(), "BuiltinShortcutPinnedTextContext", args)
                    : null;
                action ??= BuiltinShortcuts.Resolve(BuiltinShortcuts.Snapshot(), "BuiltinShortcutPinnedContext", args);
                switch (action)
                {
                    case null when Selectable && args is { Key: Key.A, KeyModifiers: KeyModifiers.Control } &&
                                   _textView is not null:
                        _textView.SelectAll();
                        args.Handled = true;
                        return;
                    case null: return;
                }

                using var input = PinnedInputContext.Use(PinnedInputContext.Current | PinnedInputOrigin.Keyboard);
                if (Presentation.Execute(action))
                {
                    args.Handled = true;
                    return;
                }

                switch (action)
                {
                    case "wheel-up": _simulatedWheel.Raise(1); break;
                    case "wheel-down": _simulatedWheel.Raise(-1); break;
                    case "thumbnail": ToggleDocumentThumbnail(); break;
                    case "select-roi": SelectDocumentThumbnailRegion(); break;
                    case "text-selection": SetSelectable(!Selectable); break;
                    case "text-ignore-format"
                        when Document is PinnedHtmlDocument: SetIgnoreFormat(!IgnoreFormat); break;
                    case "text-select-all" when Selectable: _textView?.SelectAll(); break;
                    case "annotate": _ = RunAsync(() => SetMarkEditingAsync(true)); break;
                    case "hide-mark": SetMarkHidden(!IsMarkHidden); break;
                    case "copy-plain-text":
                        _ = RunAsync(() => Presentation.CopySourceMimeAsync(PinnedSourceMimeFormat.PlainText));
                        break;
                    case "copy-html":
                        _ = RunAsync(() => Presentation.CopySourceMimeAsync(PinnedSourceMimeFormat.Html));
                        break;
                    case "copy": _ = RunAsync(CopyAsync); break;
                    case "save": _ = RunAsync(SaveRenderedImageAsync); break;
                    case "copy-file": _ = RunAsync(CopyAsFileAsync); break;
                    case "copy-all-text": _ = RunAsync(() => CopyTextAsync(Document.PlainText)); break;
                    case "hide": HideImage(); break;
                    case "close": CloseRequested?.Invoke(this, false); break;
                    case "destroy": CloseRequested?.Invoke(this, true); break;
                    case "select": SelectionRequested?.Invoke(this); break;
                    case "lock":
                        _locked = !_locked;
                        RefreshPreferencePresentation();
                        StateChanged?.Invoke(this, EventArgs.Empty);
                        break;
                    case "topmost":
                        Topmost = !Topmost;
                        RefreshPreferencePresentation();
                        break;
                    case "through" when PinnedWindowPlatform.CanSetMouseThrough(this):
                        _ = RunAsync(() =>
                        {
                            SetMouseThrough(!_mouseThrough);
                            return Task.CompletedTask;
                        }); break;
                    case "menu": Presentation.OpenMenu(menu, _content); break;
                    case "reset":
                        SetZoom(1);
                        Opacity = 1;
                        SetContentScrollOffset(default);
                        break;
                    case "selection-save":
                        _ = RunAsync(() => SaveSelectionRequested?.Invoke(this) ?? Task.CompletedTask); break;
                    default:
                        if (!PinnedImageArrangementCommands.TryResolve(action, out var arrangement)) return;
                        _ = RunAsync(() => ArrangeSelectionRequested?.Invoke(this, arrangement) ?? Task.CompletedTask);
                        break;
                }

                args.Handled = true;
            }, RoutingStrategies.Tunnel);
        }

        private bool ExecuteMouse(InputMouseGesture gesture, ContextMenu menu, double amount)
        {
            using var input = PinnedInputContext.Use(PinnedInputContext.Current | PinnedInputOrigin.Pointer);
            if (!MouseCommands.TryResolve(MouseGestureContext.Pinned, gesture, MouseCommands.SnapshotBindings(),
                    out var binding)) return false;
            var preferences = PreferencesProvider();
            // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
            switch (binding!.Action)
            {
                case MouseAction.ToggleThumbnail: ToggleDocumentThumbnail(); break;
                case MouseAction.Copy: _ = RunAsync(CopyAsync); break;
                case MouseAction.CopyRecognizedText when Document.PlainText.Length > 0:
                    _ = RunAsync(() => CopyTextAsync(Document.PlainText)); break;
                case MouseAction.CopyAndClose:
                    _ = RunAsync(async () =>
                    {
                        await CopyAsync();
                        CloseRequested?.Invoke(this, false);
                    }); break;
                case MouseAction.Save: _ = RunAsync(SaveRenderedImageAsync); break;
                case MouseAction.Hide: HideImage(); break;
                case MouseAction.Close: CloseRequested?.Invoke(this, false); break;
                case MouseAction.Destroy: CloseRequested?.Invoke(this, true); break;
                case MouseAction.ContextMenu: Presentation.OpenMenu(menu, _content); break;
                case MouseAction.ToggleSelection: SelectionRequested?.Invoke(this); break;
                case MouseAction.Zoom when !IsOperationLocked(PinnedLockRestrictions.Move):
                    SetZoom(_zoom * Math.Pow(1 + preferences.ZoomStep, amount)); break;
                case MouseAction.FineZoom when !IsOperationLocked(PinnedLockRestrictions.Move):
                    SetZoom(_zoom * Math.Pow(1 + preferences.FineZoomStep, amount)); break;
                case MouseAction.AdjustOpacity:
                    AdjustOpacity(amount * preferences.OpacityStep); break;
                case MouseAction.ActualSize when !IsOperationLocked(PinnedLockRestrictions.Move): SetZoom(1); break;
                case MouseAction.Reset when !IsOperationLocked(PinnedLockRestrictions.Move):
                    SetZoom(1);
                    Opacity = 1;
                    SetContentScrollOffset(default);
                    break;
                default: return false;
            }

            return true;
        }

        private static bool HasPlainTextCopy(MouseButton button, KeyModifiers modifiers,
            IReadOnlyList<MouseCommandBinding> bindings)
        {
            return (MouseCommands.TryResolve(MouseGestureContext.Pinned,
                        MouseGestureTracker.ClickGesture(button, modifiers, 1), bindings, out var single) &&
                    single!.Action == MouseAction.CopyRecognizedText) ||
                   (MouseCommands.TryResolve(MouseGestureContext.Pinned,
                        MouseGestureTracker.ClickGesture(button, modifiers, 2), bindings, out var twice) &&
                    twice!.Action == MouseAction.CopyRecognizedText);
        }

        private void SetZoom(double value)
        {
            ApplyDocumentZoom(Math.Clamp(value, 0.05, _isThumbnail ? double.MaxValue : 8));
        }

        private void ApplyDocumentZoom(double value)
        {
            _zoom = value;
            _layout.LayoutTransform = new ScaleTransform(_zoom, _zoom);
            RefreshFileIconSampling();
            if (_isThumbnail) ApplyDocumentThumbnailGeometry();
            DocumentAnnotationLayoutChanged(false);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        private WindowEdge? ResizeEdge(Point point)
        {
            if (!PreferencesProvider().ResizeFromBorder || IsOperationLocked(PinnedLockRestrictions.Move))
                return null;
            const double margin = 6;
            var horizontal = point.X < margin ? -1 : point.X > Bounds.Width - margin ? 1 : 0;
            var vertical = point.Y < margin ? -1 : point.Y > Bounds.Height - margin ? 1 : 0;
            return (horizontal, vertical) switch
            {
                (-1, -1) => WindowEdge.NorthWest, (1, -1) => WindowEdge.NorthEast,
                (-1, 1) => WindowEdge.SouthWest, (1, 1) => WindowEdge.SouthEast,
                (-1, 0) => WindowEdge.West, (1, 0) => WindowEdge.East,
                (0, -1) => WindowEdge.North, (0, 1) => WindowEdge.South,
                _ => null,
            };
        }
    }
}

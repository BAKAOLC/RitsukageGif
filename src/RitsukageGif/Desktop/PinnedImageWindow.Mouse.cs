using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using WindowPixelPoint = Avalonia.PixelPoint;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        private static readonly Cursor HorizontalResizeCursor = new(StandardCursorType.SizeWestEast);
        private static readonly Cursor VerticalResizeCursor = new(StandardCursorType.SizeNorthSouth);
        private static readonly Cursor DiagonalResizeCursor = new(StandardCursorType.TopLeftCorner);
        private static readonly Cursor InverseDiagonalResizeCursor = new(StandardCursorType.TopRightCorner);
        private static readonly Cursor TextSelectionCursor = new(StandardCursorType.Ibeam);
        private static readonly Cursor RegionSelectionCursor = new(StandardCursorType.Cross);
        private readonly MouseClickDispatcher _mouseClicks = new();
        private readonly MouseGestureTracker _mouseGesture = new();
        private readonly HashSet<MouseButton> _pressedButtons = [];
        private MouseAction? _activeDragAction;
        private bool _adjustingWindow;
        private int _dragHorizontalEdge, _dragVerticalEdge;
        private Point _dragStartImage;
        private WindowPixelPoint _dragStartPosition;
        private WindowPixelPoint _dragStartScreen;
        private Size _dragStartSize;
        private double _dragStartZoom;
        private static IReadOnlyList<MouseCommandBinding> MouseBindings => MouseCommands.SnapshotBindings();

        private void ConfigureMouseInput(Image image, Control content, ContextMenu menu)
        {
            image.PointerEntered += (_, _) => EnsureTextRecognition();
            content.AddHandler(ContextRequestedEvent, (_, args) => args.Handled = true, RoutingStrategies.Tunnel);
            image.PointerPressed += (_, args) =>
            {
                if (_annotation is not null) return;
                if (TryBeginTextSelection(args)) return;
                ClearTextSelection();
                var button = args.GetCurrentPoint(image).Properties.PointerUpdateKind switch
                {
                    PointerUpdateKind.LeftButtonPressed => MouseButton.Left,
                    PointerUpdateKind.RightButtonPressed => MouseButton.Right,
                    PointerUpdateKind.MiddleButtonPressed => MouseButton.Middle,
                    PointerUpdateKind.XButton1Pressed => MouseButton.XButton1,
                    PointerUpdateKind.XButton2Pressed => MouseButton.XButton2,
                    _ => MouseButton.None,
                };
                if (button == MouseButton.None) return;
                _pressedButtons.Add(button);
                _mouseClicks.Press(button, args.KeyModifiers, args.ClickCount);
                if (!_mouseGesture.Begin(button, args.KeyModifiers, args.GetPosition(image), args.ClickCount))
                {
                    args.Handled = true;
                    return;
                }

                _dragStartScreen = image.PointToScreen(args.GetPosition(image));
                _dragStartImage = args.GetPosition(image);
                _dragStartPosition = Position;
                _dragStartSize = new(Width * RenderScaling, Height * RenderScaling);
                _dragStartZoom = _zoom;
                (_dragHorizontalEdge, _dragVerticalEdge) = GetResizeEdges(args.GetPosition(this));
                args.Pointer.Capture(image);
                args.Handled = true;
            };
            image.PointerMoved += (sender, args) =>
            {
                if (_annotation is not null) return;
                EnsureTextRecognition();
                if (ContinueTextSelection(args)) return;
                if (!_mouseGesture.IsActive)
                {
                    var (horizontal, vertical) = GetResizeEdges(args.GetPosition(this));
                    var canAdjust = MouseBindings.Any(candidate => candidate.Action == MouseAction.MoveWindow &&
                                                                   candidate.Gesture.Kind != MouseGestureKind.None);
                    var blocked = horizontal == 0 && vertical == 0
                        ? IsInteractionLocked
                        : IsOperationLocked(PinnedLockedOperations.Resize);
                    image.Cursor = _regionSelectionMode
                        ? RegionSelectionCursor
                        : blocked || !canAdjust
                            ? null
                            : (horizontal, vertical) switch
                            {
                                (0, 0) => CanSelectImageText &&
                                          HitRecognizedWord(args.GetPosition(image)) >= 0
                                    ? TextSelectionCursor
                                    : null,
                                (_, 0) => HorizontalResizeCursor,
                                (0, _) => VerticalResizeCursor,
                                (-1, -1) or (1, 1) => DiagonalResizeCursor,
                                _ => InverseDiagonalResizeCursor,
                            };
                }

                if (_activeDragAction is MouseAction.SelectThumbnailRegion or MouseAction.MoveThumbnailRegion)
                {
                    if (_activeDragAction == MouseAction.SelectThumbnailRegion)
                        PreviewRegionDrag(args.GetPosition(image));
                    else MoveThumbnailRegion(args.GetPosition(image));
                    args.Handled = true;
                    return;
                }

                if (_adjustingWindow)
                {
                    UpdateWindowDrag(image.PointToScreen(args.GetPosition(image)));
                    args.Handled = true;
                    return;
                }

                if (!_mouseGesture.Move(args.GetPosition(image), 4, out var gesture)) return;
                args.Handled = true;
                var action = (_regionSelectionMode, gesture.Kind) switch
                {
                    (true, MouseGestureKind.LeftDrag) => MouseAction.SelectThumbnailRegion,
                    _ when MouseCommands.TryResolve(MouseGestureContext.Pinned, gesture, MouseBindings, out var binding)
                        => binding!.Action,
                    _ => (MouseAction?)null,
                };
                // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
                switch (action)
                {
                    case null:
                    case MouseAction.MoveWindow or MouseAction.MoveWindowIgnoringText when
                        _dragHorizontalEdge == 0 && _dragVerticalEdge == 0
                            ? IsInteractionLocked
                            : IsOperationLocked(PinnedLockedOperations.Resize):
                    case MouseAction.SelectThumbnailRegion or MouseAction.MoveThumbnailRegion when
                        IsOperationLocked(PinnedLockedOperations.Resize):
                        return;
                }

                _activeDragAction = action.Value;
                // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
                switch (action)
                {
                    case MouseAction.MoveWindow:
                    case MouseAction.MoveWindowIgnoringText:
                        _adjustingWindow = true;
                        UpdateWindowDrag(image.PointToScreen(args.GetPosition(image)));
                        break;
                    case MouseAction.SelectThumbnailRegion:
                        BeginRegionDrag(_dragStartImage);
                        PreviewRegionDrag(args.GetPosition(image));
                        break;
                    case MouseAction.MoveThumbnailRegion:
                        if (_isThumbnail)
                        {
                            BeginRegionDrag(_dragStartImage);
                            MoveThumbnailRegion(args.GetPosition(image));
                        }

                        break;
                    case MouseAction.DragContent:
                        _ = RunAsync(() => DragContentAsync(args));
                        break;
                }
            };
            image.PointerReleased += (_, args) =>
            {
                if (_annotation is not null) return;
                if (FinishTextSelection(args)) return;
                _pressedButtons.Remove(args.InitialPressMouseButton);
                var resolve = _mouseGesture.End(args.InitialPressMouseButton, out var gesture);
                if (_mouseGesture.IsActive)
                {
                    args.Handled = true;
                    return;
                }

                if (_activeDragAction == MouseAction.SelectThumbnailRegion) FinishRegionDrag(args.GetPosition(image));
                _activeDragAction = null;
                args.Pointer.Capture(null);
                _adjustingWindow = false;
                args.Handled = true;
                if (!resolve || !MouseCommands.TryResolve(MouseGestureContext.Pinned, gesture, MouseBindings,
                        out var binding)) return;
                var point = args.GetPosition(this);
                _mouseClicks.Dispatch(binding!, MouseBindings, () => ExecuteMouseGesture(gesture, point, 1, menu),
                    PlatformSettings?.GetDoubleTapTime(PointerType.Mouse) ?? TimeSpan.FromMilliseconds(500),
                    CanExecuteMouseCommand);
            };
            image.PointerCaptureLost += (_, _) =>
            {
                _selectingText = false;
                _pressedButtons.Clear();
                _mouseGesture.Reset();
                _adjustingWindow = false;
                _activeDragAction = null;
                if (_regionOutline is not null) _content.Children.Remove(_regionOutline);
            };
            image.PointerWheelChanged += (_, args) =>
            {
                if (_annotation is not null) return;
                var amount = Math.Abs(args.Delta.Y) >= Math.Abs(args.Delta.X) ? args.Delta.Y : args.Delta.X;
                if (amount == 0) return;
                args.Handled = ExecuteMouseGesture(new(MouseGestureKind.Wheel, args.KeyModifiers),
                    args.GetPosition(this), amount, menu);
            };
            KeyDown += (_, args) =>
            {
                if (IsAnnotationTextInputKey(args.Key)) return;
                if (BuiltinShortcuts.Resolve(BuiltinShortcuts.Snapshot(), "BuiltinShortcutPinnedContext", args) !=
                    "menu") return;
                menu.Open(content);
                args.Handled = true;
            };
        }

        private (int Horizontal, int Vertical) GetResizeEdges(Point point)
        {
            if (!CurrentPreferences.ResizeFromBorder || IsOperationLocked(PinnedLockedOperations.Resize)) return (0, 0);
            const double margin = 8;
            return (point.X < margin ? -1 : point.X > Width - margin ? 1 : 0,
                point.Y < margin ? -1 : point.Y > Height - margin ? 1 : 0);
        }

        private void UpdateWindowDrag(WindowPixelPoint pointer)
        {
            if (_dragHorizontalEdge == 0 && _dragVerticalEdge == 0
                    ? IsInteractionLocked
                    : IsOperationLocked(PinnedLockedOperations.Resize)) return;
            var dx = pointer.X - _dragStartScreen.X;
            var dy = pointer.Y - _dragStartScreen.Y;
            if (_dragHorizontalEdge == 0 && _dragVerticalEdge == 0)
            {
                var previous = Position;
                Position = new(_dragStartPosition.X + pointer.X - _dragStartScreen.X,
                    _dragStartPosition.Y + pointer.Y - _dragStartScreen.Y);
                MoveSelectionRequested?.Invoke(this, new(Position.X - previous.X, Position.Y - previous.Y));
                return;
            }

            var horizontal = _dragHorizontalEdge * dx / Math.Max(1, _dragStartSize.Width);
            var vertical = _dragVerticalEdge * dy / Math.Max(1, _dragStartSize.Height);
            var factor = 1 + (Math.Abs(horizontal) >= Math.Abs(vertical) ? horizontal : vertical);
            ResizeAt(new(0, 0), _dragStartZoom * Math.Max(0.01, factor));
            Position = new(_dragStartPosition.X + (int)Math.Round(_dragHorizontalEdge < 0
                    ? _dragStartSize.Width - Width * RenderScaling
                    : 0),
                _dragStartPosition.Y + (int)Math.Round(_dragVerticalEdge < 0
                    ? _dragStartSize.Height - Height * RenderScaling
                    : 0));
        }

        private void ResetImage(Point anchor)
        {
            ResizeAt(ZoomAnchor(anchor, CurrentPreferences.ResetZoomAnchor), 1);
            Opacity = 1;
            _viewport.Offset = default;
            RestoreToScreen();
        }

        private async Task CopyAndCloseAsync()
        {
            await CopyAsync();
            if (!_closed && !IsCloseLocked) Close();
        }

        private bool CanExecuteMouseCommand()
        {
            if (_closed || _mouseGesture.IsActive) return false;
            if (!OperatingSystem.IsWindows()) return _pressedButtons.Count == 0;
            return (NativeMethods.GetAsyncKeyState(1) & 0x8000) == 0 &&
                   (NativeMethods.GetAsyncKeyState(2) & 0x8000) == 0 &&
                   (NativeMethods.GetAsyncKeyState(4) & 0x8000) == 0 &&
                   (NativeMethods.GetAsyncKeyState(5) & 0x8000) == 0 &&
                   (NativeMethods.GetAsyncKeyState(6) & 0x8000) == 0;
        }

        private bool ExecuteMouseGesture(InputMouseGesture gesture, Point point, double amount, ContextMenu menu)
        {
            if (!MouseCommands.TryResolve(MouseGestureContext.Pinned, gesture, MouseBindings, out var binding))
                return false;
            // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
            switch (binding!.Action)
            {
                case MouseAction.Copy: _ = RunAsync(CopyAsync); break;
                case MouseAction.CopyAndClose: _ = RunAsync(CopyAndCloseAsync); break;
                case MouseAction.Save: _ = RunAsync(SaveAsync); break;
                case MouseAction.Hide: HideImage(); break;
                case MouseAction.Close: RequestClose(); break;
                case MouseAction.Destroy: RequestClose(true); break;
                case MouseAction.ToggleThumbnail
                    when !IsOperationLocked(PinnedLockedOperations.Resize): ToggleThumbnail(); break;
                case MouseAction.CopyRecognizedText: _ = RunAsync(CopyRecognizedTextAsync); break;
                case MouseAction.ToggleAnnotationToolbar: _ = RunAsync(ToggleAnnotationToolbarAsync); break;
                case MouseAction.ToggleSelection: SelectionRequested?.Invoke(this); break;
                case MouseAction.ContextMenu: menu.Open(_content); break;
                case MouseAction.ActualSize
                    when !IsOperationLocked(PinnedLockedOperations.Resize): ResizeAt(point, 1); break;
                case MouseAction.Zoom when !IsOperationLocked(PinnedLockedOperations.Resize):
                    if (_isThumbnail && !CurrentPreferences.ThumbnailWheelZoom) return false;
                    ResizeAt(ZoomAnchor(point, CurrentPreferences.WheelZoomAnchor),
                        _zoom * Math.Pow(1 + CurrentPreferences.ZoomStep, amount));
                    break;
                case MouseAction.FineZoom when !IsOperationLocked(PinnedLockedOperations.Resize):
                    if (_isThumbnail && !CurrentPreferences.ThumbnailWheelZoom) return false;
                    ResizeAt(ZoomAnchor(point, CurrentPreferences.WheelZoomAnchor),
                        _zoom * Math.Pow(1 + CurrentPreferences.FineZoomStep, amount));
                    break;
                case MouseAction.AdjustOpacity:
                    Opacity = Math.Clamp(Opacity + amount * CurrentPreferences.OpacityStep, 0.15, 1); break;
                case MouseAction.Reset when !IsOperationLocked(PinnedLockedOperations.Resize): ResetImage(point); break;
                default: return false;
            }

            return true;
        }
    }
}

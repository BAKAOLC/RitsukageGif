using Avalonia;
using Avalonia.Input;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSurface
    {
        private readonly MouseClickDispatcher _mouseClicks = new();
        private readonly MouseGestureTracker _mouseGesture = new();
        private bool _duplicatingAnnotation;
        private bool _mouseSelecting, _mouseChordCancelled, _mouseButtonsPressed;
        private PixelPoint _mouseStart;
        private bool _movingObject;

        private void ConfigureMouseInput(Func<PointerEventArgs, PixelPoint> sourcePoint,
            SelectionCoordinator? coordinator)
        {
            var bindings = coordinator?.MouseBindings ?? MouseCommands.SnapshotBindings();
            PointerPressed += async (_, args) =>
            {
                if (IsTextEditorSource(args.Source)) return;
                args.Handled = true;
                var properties = args.GetCurrentPoint(this).Properties;
                var button = properties.PointerUpdateKind switch
                {
                    PointerUpdateKind.LeftButtonPressed => MouseButton.Left,
                    PointerUpdateKind.RightButtonPressed => MouseButton.Right,
                    PointerUpdateKind.MiddleButtonPressed => MouseButton.Middle,
                    _ => MouseButton.None,
                };
                var clickCount = _mouseGesture.GetClickCount(args, button, this);
                if (_session.IsDrawingPolyline &&
                    (properties.IsRightButtonPressed || (properties.IsLeftButtonPressed && clickCount == 2)))
                {
                    _session.FinishPolyline();
                    _mouseGesture.Reset();
                    _mouseSelecting = false;
                    _movingObject = false;
                    _mouseChordCancelled = true;
                    _mouseButtonsPressed = AnyPressed(properties);
                    coordinator?.SetMouseInputHeld(_mouseButtonsPressed);
                    Release();
                    return;
                }

                if (_session.IsDrawingPointLine &&
                    (properties.IsLeftButtonPressed || properties.IsRightButtonPressed))
                {
                    var screenshotDoubleClick = properties.IsLeftButtonPressed && clickCount == 2 &&
                                                coordinator?.MouseContext == MouseGestureContext.Screenshot;
                    if (properties.IsLeftButtonPressed && clickCount != 2)
                        _session.ConfirmPointLine(sourcePoint(args), args.KeyModifiers.HasFlag(KeyModifiers.Shift));
                    else
                        _session.CancelGesture();
                    _mouseGesture.Reset();
                    _mouseSelecting = false;
                    _movingObject = false;
                    _mouseChordCancelled = true;
                    _mouseButtonsPressed = AnyPressed(properties);
                    coordinator?.SetMouseInputHeld(_mouseButtonsPressed);
                    Release();
                    if (screenshotDoubleClick) coordinator!.ExecuteScreenshotDoubleClick(sourcePoint(args));
                    return;
                }

                if (_session.IsPrivacyDetectionActive && properties.IsLeftButtonPressed && clickCount >= 2 &&
                    _session.HasPrivacyCandidate(sourcePoint(args)))
                {
                    _mouseGesture.Reset();
                    _mouseChordCancelled = true;
                    Release();
                    return;
                }

                if (_session.IsEditingText)
                {
                    _session.CommitTextEdit();
                    if (properties.IsRightButtonPressed) return;
                }

                if (properties.IsRightButtonPressed && _session.CanStraightenCurveAt(sourcePoint(args)))
                {
                    _mouseGesture.Reset();
                    _mouseSelecting = false;
                    _movingObject = false;
                    _mouseChordCancelled = true;
                    _mouseButtonsPressed = AnyPressed(properties);
                    coordinator?.SetMouseInputHeld(_mouseButtonsPressed);
                    Release();
                    await _session.UpdateSelectedPropertiesAsync(_session.Properties with { Curved = false });
                    return;
                }

                var duplicating = button == MouseButton.Left && !_mouseGesture.IsActive && !_mouseChordCancelled &&
                                  args.KeyModifiers.HasFlag(KeyModifiers.Alt) &&
                                  _session.ArmAnnotationDuplicate(sourcePoint(args));
                if (!duplicating && properties.IsLeftButtonPressed &&
                    (_session.Tool == ScreenshotTool.Text || clickCount == 2) &&
                    _session.BeginTextEdit(sourcePoint(args), _session.Tool != ScreenshotTool.Text))
                {
                    _mouseButtonsPressed = false;
                    coordinator?.SetMouseInputHeld(false);
                    return;
                }

                _mouseButtonsPressed = AnyPressed(properties);
                coordinator?.SetMouseInputHeld(_mouseButtonsPressed);
                _mouseClicks.Press(button, args.KeyModifiers, clickCount);
                if (!duplicating && button == MouseButton.Left && clickCount == 2 &&
                    _session.TryBeginArrowComment(sourcePoint(args)))
                    return;
                if (_mouseGesture.IsActive)
                {
                    if (button == MouseButton.Right) CancelChord(args);

                    return;
                }

                if (_mouseChordCancelled ||
                    !_mouseGesture.Begin(button, args.KeyModifiers, args.GetPosition(this), clickCount)) return;
                _mouseStart = sourcePoint(args);
                _captured = args.Pointer;
                args.Pointer.Capture(this);
                _duplicatingAnnotation = duplicating;
                if (!duplicating && button == MouseButton.Left && _session.UsesPointLineCreation &&
                    Resolve(new(MouseGestureKind.LeftClick, args.KeyModifiers), out var clickBinding) &&
                    clickBinding!.Action == MouseAction.Select &&
                    Resolve(new(MouseGestureKind.LeftDrag, args.KeyModifiers), out var dragBinding) &&
                    dragBinding!.Action == MouseAction.Select)
                {
                    _mouseSelecting = true;
                    Begin(_mouseStart, args.KeyModifiers.HasFlag(KeyModifiers.Shift));
                }
            };
            PointerMoved += (_, args) =>
            {
                if (IsTextEditorSource(args.Source)) return;
                args.Handled = true;
                var pointerPosition = sourcePoint(args);
                _pointerPosition = pointerPosition;
                _session.NotifyHover(_pointerPosition);
                RefreshCursor();
                InvalidateAnnotations();
                var properties = args.GetCurrentPoint(this).Properties;
                _mouseButtonsPressed = AnyPressed(properties);
                coordinator?.SetMouseInputHeld(_mouseButtonsPressed);
                if (_mouseGesture.IsActive && properties.PointerUpdateKind == PointerUpdateKind.RightButtonPressed)
                    CancelChord(args);
                if (_mouseChordCancelled || _session.IsBusy) return;
                if (_duplicatingAnnotation)
                {
                    if (properties.IsLeftButtonPressed)
                        _session.MoveAnnotationDuplicate(pointerPosition, args.KeyModifiers.HasFlag(KeyModifiers.Alt));
                    return;
                }

                if (_session.IsDrawingPolyline && !_mouseSelecting)
                    _session.MovePolylinePreview(sourcePoint(args), args.KeyModifiers.HasFlag(KeyModifiers.Shift));
                if (_session.IsDrawingPointLine && !_mouseSelecting)
                    _session.MovePointLinePreview(sourcePoint(args), args.KeyModifiers.HasFlag(KeyModifiers.Shift));
                if (_session.IsPrivacyDetectionActive)
                {
                    if (_mouseGesture.Move(args.GetPosition(this), DragThreshold, out var privacyGesture) &&
                        privacyGesture.Kind == MouseGestureKind.LeftDrag &&
                        Resolve(privacyGesture, out var privacyBinding) && privacyBinding!.Action == MouseAction.Select)
                        _session.BeginPrivacyDrag(_mouseStart);
                    _session.MovePrivacyDrag(sourcePoint(args));
                    return;
                }

                if (_mouseGesture.Move(args.GetPosition(this), DragThreshold, out var gesture) && !_mouseSelecting &&
                    Resolve(gesture, out var binding) &&
                    binding!.Action == MouseAction.Select)
                {
                    _mouseSelecting = true;
                    Begin(_mouseStart, args.KeyModifiers.HasFlag(KeyModifiers.Shift));
                }

                if (!_mouseSelecting) return;
                if (_movingObject)
                    _session.MoveObjectWithModifiers(sourcePoint(args), args.KeyModifiers.HasFlag(KeyModifiers.Shift));
                else _session.MoveWithModifiers(sourcePoint(args), args.KeyModifiers.HasFlag(KeyModifiers.Shift));
            };
            PointerReleased += async (_, args) =>
            {
                if (IsTextEditorSource(args.Source)) return;
                args.Handled = true;
                var properties = args.GetCurrentPoint(this).Properties;
                _mouseButtonsPressed = AnyPressed(properties);
                coordinator?.SetMouseInputHeld(_mouseButtonsPressed);
                if (_mouseChordCancelled)
                {
                    if (!AnyPressed(properties))
                    {
                        _mouseChordCancelled = false;
                        Release();
                    }

                    return;
                }

                if (_duplicatingAnnotation && args.InitialPressMouseButton == MouseButton.Left)
                {
                    _duplicatingAnnotation = false;
                    _mouseGesture.Reset();
                    _completing = true;
                    try
                    {
                        var completion = _session.FinishAnnotationDuplicateAsync();
                        if (!_mouseButtonsPressed) Release();
                        await completion;
                    }
                    finally
                    {
                        _completing = false;
                    }

                    return;
                }

                var active = _mouseGesture.IsActive;
                var click = _mouseGesture.End(args.InitialPressMouseButton, out var gesture);
                if (!active || _mouseGesture.IsActive)
                {
                    if (!_mouseButtonsPressed) Release();
                    return;
                }

                _completing = true;
                try
                {
                    switch (click)
                    {
                        case true when coordinator is { ScreenshotMode: true } &&
                                       gesture is
                                       {
                                           Kind: MouseGestureKind.MiddleClick or MouseGestureKind.MiddleDoubleClick,
                                       }:
                            coordinator.ExecuteScreenshotMiddleClick(sourcePoint(args));
                            return;
                        case false when args.InitialPressMouseButton == MouseButton.Left && _session.IsPrivacyDragging:
                            _session.EndPrivacyDrag(sourcePoint(args));
                            return;
                        case true when Resolve(gesture, out var binding):
                            if (binding!.Action == MouseAction.Select && !_session.IsBusy)
                            {
                                if (_session.IsPrivacyDetectionActive)
                                {
                                    _session.TogglePrivacyCandidate(sourcePoint(args));
                                    return;
                                }

                                if (!_mouseSelecting)
                                {
                                    if (!_session.HitTestAnnotation(sourcePoint(args), true) &&
                                        await _session.TryCreateQuickMarkAsync(sourcePoint(args))) return;
                                    _mouseSelecting = true;
                                    Begin(_session.UsesPointLineCreation ? _mouseStart : sourcePoint(args),
                                        args.KeyModifiers.HasFlag(KeyModifiers.Shift));
                                }
                            }
                            else
                            {
                                var point = sourcePoint(args);
                                _mouseClicks.Dispatch(binding, bindings, () => Execute(binding.Action, point),
                                    Application.Current?.PlatformSettings?.GetDoubleTapTime(PointerType.Mouse) ??
                                    TimeSpan.FromMilliseconds(500),
                                    () => !_mouseButtonsPressed && !_mouseGesture.IsActive && !_mouseChordCancelled);
                            }

                            break;
                    }

                    if (_mouseSelecting)
                    {
                        if (_movingObject)
                        {
                            await _session.EndObjectMoveAsync();
                        }
                        else
                        {
                            _session.MoveWithModifiers(sourcePoint(args),
                                args.KeyModifiers.HasFlag(KeyModifiers.Shift));
                            await _session.EndAsync(sourcePoint(args));
                        }
                    }
                }
                finally
                {
                    _mouseSelecting = false;
                    _movingObject = false;
                    if (!_mouseButtonsPressed) Release();
                    _completing = false;
                }
            };
            PointerWheelChanged += async (_, args) =>
            {
                if (IsTextEditorSource(args.Source)) return;
                args.Handled = true;
                if (_mouseGesture.IsActive || _mouseChordCancelled || args.Delta is { X: 0, Y: 0 }) return;
                var kind = args.Delta.Y > 0 ? MouseGestureKind.WheelUp :
                    args.Delta.Y < 0 ? MouseGestureKind.WheelDown :
                    args.Delta.X > 0 ? MouseGestureKind.WheelRight : MouseGestureKind.WheelLeft;
                if (Resolve(new(kind, args.KeyModifiers), out var binding)) Execute(binding!.Action, sourcePoint(args));
                else if (coordinator is { ScreenshotMode: true } &&
                         _session is { Tool: ScreenshotTool.Select, HasSelectedAnnotation: false })
                    coordinator.ScreenshotWheel(sourcePoint(args), args.Delta.Y);
                else if (args.Delta.Y != 0)
                    await _session.AdjustToolWheelWithModifiersAsync(Math.Sign(args.Delta.Y), args.KeyModifiers);
            };
            return;

            bool Resolve(InputMouseGesture gesture, out MouseCommandBinding? binding)
            {
                return MouseCommands.TryResolve(MouseGestureContext.Annotation, gesture, bindings, out binding);
            }

            void CancelChord(PointerEventArgs args)
            {
                if (!Resolve(new(MouseGestureKind.RightClick, args.KeyModifiers), out var cancel) ||
                    cancel!.Action != MouseAction.Cancel) return;
                _session.CancelWork();
                _duplicatingAnnotation = false;
                _mouseGesture.Reset();
                _mouseSelecting = false;
                _mouseChordCancelled = true;
            }

            void Begin(PixelPoint point, bool shift)
            {
                _movingObject = _session.BeginObjectMove(point, true);
                if (_movingObject) return;
                if (_session.Tool == ScreenshotTool.Select) _session.ClearAnnotationSelection();
                else _session.BeginWithModifiers(point, shift);
            }

            void Execute(MouseAction action, PixelPoint point)
            {
                if (coordinator is not null)
                {
                    coordinator.ExecuteMouseAction(action, point);
                    return;
                }

                // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
                switch (action)
                {
                    case MouseAction.Cancel:
                        _session.CancelWork();
                        _session.ClearAnnotationSelection();
                        _session.Tool = ScreenshotTool.Select;
                        break;
                    case MouseAction.Undo: _session.Undo(); break;
                    case MouseAction.Redo: _session.Redo(); break;
                    case MouseAction.Copy: _session.RequestComplete(ScreenshotOutputAction.Copy); break;
                    case MouseAction.Save: _session.RequestComplete(ScreenshotOutputAction.Save); break;
                }
            }
        }

        private void Release()
        {
            var pointer = _captured;
            _captured = null;
            pointer?.Capture(null);
        }

        private static bool AnyPressed(PointerPointProperties properties)
        {
            return properties.IsLeftButtonPressed || properties.IsRightButtonPressed ||
                   properties.IsMiddleButtonPressed;
        }
    }
}

using Avalonia.Controls;
using Avalonia.Input;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSurface
    {
        private readonly MouseClickDispatcher _mouseClicks = new();
        private readonly MouseGestureTracker _mouseGesture = new();
        private bool _mouseSelecting, _mouseChordCancelled, _mouseButtonsPressed;
        private PixelPoint _mouseStart;

        private void ConfigureMouseInput(Func<PointerEventArgs, PixelPoint> sourcePoint,
            SelectionCoordinator? coordinator)
        {
            var bindings = coordinator?.MouseBindings ?? MouseCommands.SnapshotBindings();
            PointerPressed += (_, args) =>
            {
                if (IsTextEditorSource(args.Source)) return;
                args.Handled = true;
                var properties = args.GetCurrentPoint(this).Properties;
                if (_session.IsEditingText)
                {
                    _session.CommitTextEdit();
                    if (properties.IsRightButtonPressed) return;
                }

                if (properties.IsLeftButtonPressed &&
                    (_session.Tool == ScreenshotTool.Text || args.ClickCount == 2) &&
                    _session.BeginTextEdit(sourcePoint(args), _session.Tool != ScreenshotTool.Text))
                {
                    _mouseButtonsPressed = false;
                    coordinator?.SetMouseInputHeld(false);
                    return;
                }

                _mouseButtonsPressed = AnyPressed(properties);
                coordinator?.SetMouseInputHeld(_mouseButtonsPressed);
                var button = properties.PointerUpdateKind switch
                {
                    PointerUpdateKind.LeftButtonPressed => MouseButton.Left,
                    PointerUpdateKind.RightButtonPressed => MouseButton.Right,
                    PointerUpdateKind.MiddleButtonPressed => MouseButton.Middle,
                    _ => MouseButton.None,
                };
                _mouseClicks.Press(button, args.KeyModifiers, args.ClickCount);
                if (button == MouseButton.Left && args.ClickCount == 2 &&
                    _session.TryBeginArrowComment(sourcePoint(args)))
                    return;
                if (_mouseGesture.IsActive)
                {
                    if (button == MouseButton.Right) CancelChord(args);

                    return;
                }

                if (_mouseChordCancelled ||
                    !_mouseGesture.Begin(button, args.KeyModifiers, args.GetPosition(this), args.ClickCount)) return;
                _mouseStart = sourcePoint(args);
                _captured = args.Pointer;
                args.Pointer.Capture(this);
            };
            PointerMoved += (_, args) =>
            {
                if (IsTextEditorSource(args.Source)) return;
                args.Handled = true;
                _pointerPosition = sourcePoint(args);
                _session.NotifyHover(_pointerPosition);
                InvalidateAnnotations();
                var properties = args.GetCurrentPoint(this).Properties;
                _mouseButtonsPressed = AnyPressed(properties);
                coordinator?.SetMouseInputHeld(_mouseButtonsPressed);
                if (_mouseGesture.IsActive && properties.PointerUpdateKind == PointerUpdateKind.RightButtonPressed)
                    CancelChord(args);
                if (_mouseChordCancelled || _session.IsBusy) return;
                if (_session.IsPrivacyDetectionActive)
                {
                    _mouseGesture.Move(args.GetPosition(this), 4, out var _);
                    return;
                }

                if (_mouseGesture.Move(args.GetPosition(this), 4, out var gesture) &&
                    Resolve(gesture, out var binding) &&
                    binding!.Action == MouseAction.Select)
                {
                    _mouseSelecting = true;
                    Begin(_mouseStart);
                }

                if (!_mouseSelecting) return;
                if (_session.Tool == ScreenshotTool.Select) _session.MoveObject(sourcePoint(args));
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
                    if (click && Resolve(gesture, out var binding))
                    {
                        if (binding!.Action == MouseAction.Select && !_session.IsBusy)
                        {
                            if (_session.IsPrivacyDetectionActive)
                            {
                                _session.TogglePrivacyCandidate(sourcePoint(args));
                                return;
                            }

                            if (await _session.TryCreateQuickMarkAsync(sourcePoint(args))) return;
                            _mouseSelecting = true;
                            Begin(sourcePoint(args));
                        }
                        else
                        {
                            var point = sourcePoint(args);
                            _mouseClicks.Dispatch(binding, bindings, () => Execute(binding.Action, point),
                                TopLevel.GetTopLevel(this)?.PlatformSettings?.GetDoubleTapTime(PointerType.Mouse) ??
                                TimeSpan.FromMilliseconds(500),
                                () => !_mouseButtonsPressed && !_mouseGesture.IsActive && !_mouseChordCancelled);
                        }
                    }

                    if (_mouseSelecting)
                    {
                        if (_session.Tool == ScreenshotTool.Select)
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
                else if (args.Delta.Y != 0) await _session.AdjustToolWheelAsync(Math.Sign(args.Delta.Y));
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
                _mouseGesture.Reset();
                _mouseSelecting = false;
                _mouseChordCancelled = true;
            }

            void Begin(PixelPoint point)
            {
                if (_session.Tool == ScreenshotTool.Select)
                {
                    if (!_session.BeginObjectMove(point)) _session.ClearAnnotationSelection();
                }
                else
                {
                    _session.Begin(point);
                }
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

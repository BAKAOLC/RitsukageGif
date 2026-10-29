using Avalonia.Input;

namespace RitsukageGif.Desktop
{
    internal sealed partial class OverlayWindow
    {
        private readonly MouseClickDispatcher _mouseClicks = new();
        private readonly MouseGestureTracker _mouseGesture = new();
        private KeyModifiers _mouseModifiers;
        private bool _mouseSelecting, _mouseChordCancelled, _mouseButtonsPressed;
        private PixelPoint _mouseStart;

        private void ConfigureMouseInput(Func<PointerEventArgs, PixelPoint> sourcePoint)
        {
            _canvas.PointerEntered += (_, args) => Move(args);
            _canvas.PointerMoved += (_, args) => Move(args);
            _canvas.PointerPressed += (_, args) =>
            {
                var properties = args.GetCurrentPoint(this).Properties;
                _mouseButtonsPressed = AnyPressed(properties);
                _coordinator.SetMouseInputHeld(_mouseButtonsPressed);
                var button = properties.PointerUpdateKind switch
                {
                    PointerUpdateKind.LeftButtonPressed => MouseButton.Left,
                    PointerUpdateKind.RightButtonPressed => MouseButton.Right,
                    PointerUpdateKind.MiddleButtonPressed => MouseButton.Middle,
                    _ => MouseButton.None,
                };
                _mouseClicks.Press(button, args.KeyModifiers, args.ClickCount);
                if (_mouseGesture.IsActive)
                {
                    if (button == MouseButton.Right) CancelChord(args);

                    args.Handled = true;
                    return;
                }

                if (_mouseChordCancelled ||
                    !_mouseGesture.Begin(button, args.KeyModifiers, args.GetPosition(this), args.ClickCount))
                {
                    args.Handled = true;
                    return;
                }

                _mouseStart = sourcePoint(args);
                _mouseModifiers = args.KeyModifiers;
                Capture(args.Pointer);
                args.Handled = true;
            };
            _canvas.PointerReleased += (_, args) =>
            {
                args.Handled = true;
                var properties = args.GetCurrentPoint(this).Properties;
                _mouseButtonsPressed = AnyPressed(properties);
                _coordinator.SetMouseInputHeld(_mouseButtonsPressed);
                if (_mouseChordCancelled)
                {
                    if (!AnyPressed(properties))
                    {
                        _mouseChordCancelled = false;
                        ReleaseCapture();
                    }

                    return;
                }

                var wasSelecting = _mouseSelecting;
                var wasActive = _mouseGesture.IsActive;
                var click = _mouseGesture.End(args.InitialPressMouseButton, out var gesture);
                if (wasActive && !_mouseGesture.IsActive)
                {
                    _mouseSelecting = false;
                    if (wasSelecting)
                    {
                        _coordinator.PointerInput(sourcePoint(args), false, false,
                            _mouseModifiers.HasFlag(KeyModifiers.Shift));
                    }
                    else if (click && Resolve(gesture, out var binding))
                    {
                        if (binding!.Action == MouseAction.Select)
                        {
                            _coordinator.PointerInput(sourcePoint(args), true, false,
                                _mouseModifiers.HasFlag(KeyModifiers.Shift), true);
                            _coordinator.PointerInput(sourcePoint(args), false, false,
                                _mouseModifiers.HasFlag(KeyModifiers.Shift));
                        }
                        else
                        {
                            var point = sourcePoint(args);
                            _mouseClicks.Dispatch(binding, _coordinator.MouseBindings,
                                () => _coordinator.ExecuteMouseAction(binding.Action, point),
                                PlatformSettings?.GetDoubleTapTime(PointerType.Mouse) ?? TimeSpan.FromMilliseconds(500),
                                () => !_mouseButtonsPressed && !_mouseGesture.IsActive && !_mouseChordCancelled);
                        }
                    }
                }

                if (!AnyPressed(properties)) ReleaseCapture();
            };
            _canvas.PointerWheelChanged += (_, args) =>
            {
                if (_mouseGesture.IsActive || _mouseChordCancelled)
                {
                    args.Handled = true;
                    return;
                }

                var kind = args.Delta.Y > 0 ? MouseGestureKind.WheelUp :
                    args.Delta.Y < 0 ? MouseGestureKind.WheelDown :
                    args.Delta.X > 0 ? MouseGestureKind.WheelRight : MouseGestureKind.WheelLeft;
                if (args.Delta is { X: 0, Y: 0 }) return;
                if (Resolve(new(kind, args.KeyModifiers), out var binding))
                    _coordinator.ExecuteMouseAction(binding!.Action, sourcePoint(args));
                else if (args.KeyModifiers == KeyModifiers.None && args.Delta.Y != 0)
                    _coordinator.CycleCandidate(sourcePoint(args), args.Delta.Y > 0);
                args.Handled = true;
            };
            return;

            void Capture(IPointer pointer)
            {
                _captured = pointer;
                pointer.Capture(_canvas);
            }

            void Move(PointerEventArgs args)
            {
                args.Handled = true;
                var properties = args.GetCurrentPoint(this).Properties;
                _mouseButtonsPressed = AnyPressed(properties);
                _coordinator.SetMouseInputHeld(_mouseButtonsPressed);
                if (_mouseGesture.IsActive && properties.PointerUpdateKind == PointerUpdateKind.RightButtonPressed)
                    CancelChord(args);
                if (_mouseChordCancelled) return;
                if (_mouseGesture.Move(args.GetPosition(this), 4, out var gesture) &&
                    Resolve(gesture, out var binding) &&
                    binding!.Action == MouseAction.Select)
                {
                    _mouseSelecting = true;
                    _coordinator.PointerInput(_mouseStart, true, false, _mouseModifiers.HasFlag(KeyModifiers.Shift),
                        true);
                }

                if (_mouseGesture.IsActive && !_mouseSelecting)
                {
                    _coordinator.HoverInput(sourcePoint(args), _mouseModifiers.HasFlag(KeyModifiers.Shift));
                    return;
                }

                _coordinator.PointerInput(sourcePoint(args), _mouseSelecting, false,
                    (_mouseGesture.IsActive ? _mouseModifiers : args.KeyModifiers).HasFlag(KeyModifiers.Shift));
            }

            void CancelChord(PointerEventArgs args)
            {
                if (!Resolve(new(MouseGestureKind.RightClick, args.KeyModifiers), out var cancel) ||
                    cancel!.Action != MouseAction.Cancel) return;
                _coordinator.CancelMouseDrag();
                _mouseGesture.Reset();
                _mouseSelecting = false;
                _mouseChordCancelled = true;
                Capture(args.Pointer);
            }
        }

        private bool Resolve(InputMouseGesture gesture, out MouseCommandBinding? binding)
        {
            return MouseCommands.TryResolve(_coordinator.MouseContext, gesture, _coordinator.MouseBindings,
                out binding);
        }

        private void ResetMouseInput()
        {
            _mouseGesture.Reset();
            _mouseSelecting = false;
            _mouseChordCancelled = false;
            _mouseButtonsPressed = false;
        }

        private static bool AnyPressed(PointerPointProperties properties)
        {
            return properties.IsLeftButtonPressed || properties.IsRightButtonPressed ||
                   properties.IsMiddleButtonPressed;
        }
    }
}

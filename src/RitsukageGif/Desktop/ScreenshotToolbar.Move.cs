using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotToolbar
    {
        private static readonly Cursor MoveCursor = new(StandardCursorType.SizeAll);
        private readonly MouseGestureTracker _moveClicks = new();
        private Point? _manualPosition;
        private Point _toolbarDragOrigin;
        private Point? _toolbarDragStart;
        private SelectionCoordinator? _desktopPlacement;
        private OverlayWindow? _desktopOverlay;
        private readonly ScaleTransform _desktopTransform = new();
        private bool _desktopPrimary;
        private bool _desktopIntersects;
        private bool _desktopInteractive;
        private bool _hideWhileDrawing;
        private bool _manualTip;
        private Point _displayedPosition;
        private double _displayedScale = 1;

        private bool IsManuallyPlaced => _desktopPlacement?.ManualScreenshotToolbar is not null ||
                                         _manualPosition is not null;

        internal bool DesktopAbove { get; private set; }

        internal Point DesktopPosition => new(_monitor.X + _displayedPosition.X * Scale,
            _monitor.Y + _displayedPosition.Y * Scale);

        internal void UseDesktopPlacement(SelectionCoordinator coordinator, OverlayWindow overlay)
        {
            _desktopPlacement = coordinator;
            _desktopOverlay = overlay;
            RenderTransformOrigin = RelativePoint.TopLeft;
        }

        private void PlaceDesktop(Point position, double scale)
        {
            _displayedPosition = position;
            _displayedScale = scale;
            Canvas.SetLeft(this, position.X);
            Canvas.SetTop(this, position.Y);
            if (scale.Equals(1d))
            {
                RenderTransform = null;
                return;
            }

            if (!_desktopTransform.ScaleX.Equals(scale))
            {
                _desktopTransform.ScaleX = scale;
                _desktopTransform.ScaleY = scale;
            }

            RenderTransform = _desktopTransform;
        }

        private void SetDesktopInteraction(bool primary, bool interactive, bool visible)
        {
            if (_desktopInteractive && !interactive) HideDesktopPanels();
            _desktopPrimary = primary;
            _desktopInteractive = interactive;
            _desktopIntersects = visible;
            if (_manualTip != IsManuallyPlaced) RefreshMoveHandleTip(_moveHandle);
            UpdateDesktopVisibility();
        }

        private void UpdateDesktopVisibility()
        {
            Opacity = _desktopIntersects && !_hideWhileDrawing ? 1d : 0d;
            IsHitTestVisible = _desktopInteractive && !_hideWhileDrawing;
        }

        internal void HideDesktopPanels()
        {
            HideToolGroups();
            _moreCommands.Hide();
            _outputProperties.Hide();
            _propertyPresenter.HideFlyouts();
        }

        private bool TryGetManualPosition(Size viewport, Size measured, out Point position)
        {
            if (_manualPosition is not { } current)
            {
                position = default;
                return false;
            }

            position = new(Math.Clamp(current.X, 0, Math.Max(0, viewport.Width - measured.Width)),
                Math.Clamp(current.Y, 0, Math.Max(0, viewport.Height - measured.Height)));
            _manualPosition = position;
            return true;
        }

        private Button CreateMoveHandle()
        {
            var handle = SmallButton("ToolbarMove", () => { }, ToolbarIcon.FindDefinition("ToolbarMove") ??
                                                               ToolbarIcon.GetDefinition("AnimationSelection"));
            handle.Width = 14;
            handle.Padding = new(2, 0);
            handle.Cursor = MoveCursor;
            handle.GetValue(TooltipBindingProperty)?.Dispose();
            handle.ClearValue(TooltipBindingProperty);
            RefreshMoveHandleTip(handle);
            if (handle.Content is Control icon)
            {
                icon.Width = 8;
                icon.Height = 14;
            }

            handle.AddHandler(PointerPressedEvent, (_, args) =>
            {
                args.Handled = true;
                if (IsDocked) return;
                var properties = args.GetCurrentPoint(handle).Properties;
                var button = properties.PointerUpdateKind.GetMouseButton();
                if (button == MouseButton.None) return;
                var reset = _moveClicks.GetClickCount(args, button, handle) == 2;
                if (_desktopPlacement is { } coordinator && _desktopOverlay is { } overlay)
                {
                    if (reset)
                    {
                        coordinator.ResetScreenshotToolbar();
                        RefreshMoveHandleTip(handle);
                    }
                    else if (button == MouseButton.Left && IsOnlyLeftPressed(properties))
                    {
                        coordinator.BeginScreenshotToolbarDrag(overlay, this, DesktopPointer(overlay, args));
                        args.Pointer.Capture(handle);
                    }

                    return;
                }

                if (reset)
                {
                    if (_manualPosition is null) return;
                    _manualPosition = null;
                    RefreshMoveHandleTip(handle);
                    _placedSize = default;
                    Refresh(_placedRegion, _placementMouse, _ownsToolbar);
                    return;
                }

                if (button != MouseButton.Left || !IsOnlyLeftPressed(properties)) return;
                _toolbarDragStart = args.GetPosition(this.GetVisualParent());
                _toolbarDragOrigin = new(Margin.Left, Margin.Top);
                args.Pointer.Capture(handle);
            }, RoutingStrategies.Tunnel, true);
            handle.AddHandler(PointerMovedEvent, (_, args) =>
            {
                if (!ReferenceEquals(args.Pointer.Captured, handle)) return;
                var properties = args.GetCurrentPoint(handle).Properties;
                if (properties.PointerUpdateKind != PointerUpdateKind.Other || !IsOnlyLeftPressed(properties)) return;
                if (_desktopPlacement is { } coordinator && _desktopOverlay is { } overlay)
                {
                    args.Handled = true;
                    coordinator.MoveScreenshotToolbar(overlay, DesktopPointer(overlay, args));
                    return;
                }

                if (_toolbarDragStart is not { } start) return;
                args.Handled = true;
                var current = args.GetPosition(this.GetVisualParent());
                var wasAutomatic = _manualPosition is null;
                _manualPosition = _toolbarDragOrigin + (current - start);
                if (wasAutomatic) RefreshMoveHandleTip(handle);
                if (TryGetManualPosition(new(_monitor.Width / Scale, _monitor.Height / Scale), Bounds.Size,
                        out var position)) Margin = new(position.X, position.Y, 0, 0);
            }, RoutingStrategies.Tunnel, true);
            handle.AddHandler(PointerReleasedEvent, (_, args) =>
            {
                args.Handled = true;
                if (args.GetCurrentPoint(handle).Properties.IsLeftButtonPressed) return;
                _toolbarDragStart = null;
                if (ReferenceEquals(args.Pointer.Captured, handle)) args.Pointer.Capture(null);
                if (_desktopOverlay is { } overlay) _desktopPlacement?.EndScreenshotToolbarDrag(overlay);
            }, RoutingStrategies.Tunnel, true);
            handle.PointerCaptureLost += (_, _) =>
            {
                _toolbarDragStart = null;
                if (_desktopOverlay is { } overlay) _desktopPlacement?.EndScreenshotToolbarDrag(overlay);
            };
            return handle;
        }

        private static bool IsOnlyLeftPressed(PointerPointProperties properties)
        {
            return properties is
            {
                IsLeftButtonPressed: true, IsMiddleButtonPressed: false, IsRightButtonPressed: false,
                IsXButton1Pressed: false, IsXButton2Pressed: false,
            };
        }

        internal bool UpdateDesktopDragPosition()
        {
            if (_desktopPlacement?.ManualScreenshotToolbar is not { } state || !IsVisible ||
                _placedSize.Width <= 0 || _desktopOverlay is not { } overlay) return false;
            var position = new Point((state.Position.X - _monitor.X) / Scale,
                (state.Position.Y - _monitor.Y) / Scale);
            var fit = state.Scale / Scale;
            ArrangeFloatingLayers(state.Above);
            _moreCommands.SetPlacement(state.Above ? PlacementMode.Top : PlacementMode.Bottom);
            PlaceDesktop(position, fit);
            var intersects = new Rect(position, new Size(_placedSize.Width * fit, _placedSize.Height * fit))
                .Intersects(new(new(), new Size(_monitor.Width / Scale, _monitor.Height / Scale)));
            var primary = _desktopPlacement.OwnsDesktopScreenshotToolbar(overlay, _placedRegion);
            SetDesktopInteraction(primary,
                primary || (!_desktopPlacement.ScreenshotToolbarDragInProgress && intersects),
                intersects);
            return true;
        }

        private static Point DesktopPointer(OverlayWindow overlay, PointerEventArgs args)
        {
            var local = args.GetPosition(overlay);
            return new(overlay.Region.X + local.X * overlay.ScreenshotToolbarScale,
                overlay.Region.Y + local.Y * overlay.ScreenshotToolbarScale);
        }

        private void RefreshMoveHandleTip(Button handle)
        {
            _manualTip = IsManuallyPlaced;
            ToolTip.SetTip(handle, UiText.Get(_manualTip ? "ToolbarMoveHint" : "ToolbarMoveAutomaticHint"));
        }
    }
}

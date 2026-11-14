using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using WindowPixelPoint = Avalonia.PixelPoint;

namespace RitsukageGif.Desktop
{
    internal sealed class PinnedMouseThroughToolbar : Window
    {
        private readonly Border _frame;
        private readonly Slider _opacity;
        private readonly Window _owner;
        private readonly IPinnedWindow _pin;
        private IPointer? _dragPointer;
        private WindowPixelPoint _dragStart;
        private WindowPixelPoint _ownerStart;
        private bool _readingOpacity;
        private bool _scalePlacementQueued;

        internal PinnedMouseThroughToolbar(IPinnedWindow pin, Action exit)
        {
            _pin = pin;
            _owner = pin.Window;
            ShowInTaskbar = false;
            ShowActivated = false;
            WindowDecorations = WindowDecorations.None;
            WindowStartupLocation = WindowStartupLocation.Manual;
            CanResize = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            this[!TopmostProperty] = _owner[!TopmostProperty];
            Background = Brushes.Transparent;
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent];

            var move = ScreenshotToolbar.SmallButton("ToolbarMove");
            move.Width = 18;
            move.Padding = new(2, 0);
            move.Cursor = new(StandardCursorType.SizeAll);
            if (move.Content is Control icon)
            {
                icon.Width = 8;
                icon.Height = 14;
            }

            move.AddHandler(PointerPressedEvent, BeginDrag, RoutingStrategies.Tunnel);
            move.AddHandler(PointerMovedEvent, Drag, RoutingStrategies.Tunnel);
            move.AddHandler(PointerReleasedEvent, EndDrag, RoutingStrategies.Tunnel);
            move.PointerCaptureLost += (_, _) => _dragPointer = null;
            var divider = new Border
            {
                Width = 1, Height = 18, Margin = new(2, 0), VerticalAlignment = VerticalAlignment.Center,
                Background = AppTheme.Outline,
            };
            var label = new TextBlock
            {
                Margin = new(2, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = AppTheme.Ink,
            };
            label.Bind(TextBlock.TextProperty, LiveText.Resource("PinnedToolbarOpacity"));
            _opacity = new()
            {
                Minimum = 0, Maximum = 100, TickFrequency = 1, SmallChange = 1, IsSnapToTickEnabled = true,
                MinWidth = 120, Height = 32, VerticalAlignment = VerticalAlignment.Center,
            };
            _opacity.Bind(ToolTip.TipProperty, LiveText.Resource("PinnedMouseThroughOpacity"));
            _opacity.Bind(AutomationProperties.NameProperty, LiveText.Resource("PinnedMouseThroughOpacity"));
            _opacity.ValueChanged += OpacityChanged;
            var close = ScreenshotToolbar.SmallButton("PinnedMouseThroughExit", exit,
                ToolbarIcon.GetDefinition("PinnedMenuClose"));
            _frame = new()
            {
                Padding = new(3), CornerRadius = new(8), Background = AppTheme.Surface,
                BorderBrush = AppTheme.Outline, BorderThickness = new(1),
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 2,
                    Children = { move, divider, label, _opacity, close },
                },
            };
            Content = _frame;
            _owner.PositionChanged += Reposition;
            _owner.SizeChanged += Reposition;
            _owner.ScalingChanged += Reposition;
            _owner.PropertyChanged += OwnerPropertyChanged;
            _owner.Screens.Changed += Reposition;
            ScalingChanged += QueueScalePlacement;
            SizeChanged += Reposition;
            Closed += (_, _) =>
            {
                _owner.PositionChanged -= Reposition;
                _owner.SizeChanged -= Reposition;
                _owner.ScalingChanged -= Reposition;
                _owner.PropertyChanged -= OwnerPropertyChanged;
                _owner.Screens.Changed -= Reposition;
            };
        }

        internal void Reveal()
        {
            if (IsVisible) return;
            ReadOpacity();
            _frame.Measure(Size.Infinity);
            Place();
            Show(_owner);
        }

        private void OwnerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
        {
            if (change.Property == OpacityProperty) ReadOpacity();
        }

        private void ReadOpacity()
        {
            _readingOpacity = true;
            _opacity.Value = _owner.Opacity * 100;
            _readingOpacity = false;
        }

        private void OpacityChanged(object? sender, RangeBaseValueChangedEventArgs args)
        {
            if (!_readingOpacity) _owner.Opacity = args.NewValue / 100;
        }

        private void BeginDrag(object? sender, PointerPressedEventArgs args)
        {
            if (sender is not Button move || _dragPointer is not null ||
                !args.GetCurrentPoint(move).Properties.IsLeftButtonPressed) return;
            _dragPointer = args.Pointer;
            _dragStart = this.PointToScreen(args.GetPosition(this));
            _ownerStart = _owner.Position;
            args.Pointer.Capture(move);
            args.Handled = true;
        }

        private void Drag(object? sender, PointerEventArgs args)
        {
            if (!ReferenceEquals(_dragPointer, args.Pointer)) return;
            var point = this.PointToScreen(args.GetPosition(this));
            _owner.Position = new(_ownerStart.X + point.X - _dragStart.X, _ownerStart.Y + point.Y - _dragStart.Y);
            args.Handled = true;
        }

        private void EndDrag(object? sender, PointerReleasedEventArgs args)
        {
            if (!ReferenceEquals(_dragPointer, args.Pointer) || args.InitialPressMouseButton != MouseButton.Left)
                return;
            _dragPointer = null;
            args.Pointer.Capture(null);
            args.Handled = true;
        }

        private void Reposition(object? sender, EventArgs args)
        {
            if (IsVisible) Place();
        }

        private void QueueScalePlacement(object? sender, EventArgs args)
        {
            if (_scalePlacementQueued) return;
            _scalePlacementQueued = true;
            Dispatcher.UIThread.Post(() =>
            {
                _scalePlacementQueued = false;
                if (IsVisible) Place();
            }, DispatcherPriority.Render);
        }

        private void Place()
        {
            var pin = _pin.SelectionLayoutBounds;
            var center = new PixelPoint(pin.X + pin.Width / 2, pin.Y + pin.Height / 2);
            Screen? nearest = null;
            var distance = long.MaxValue;
            foreach (var screen in _owner.Screens.All)
            {
                var bounds = screen.Bounds;
                var horizontal = Math.Max(0L, Math.Max((long)bounds.X - center.X, (long)center.X - bounds.Right + 1));
                var vertical = Math.Max(0L, Math.Max((long)bounds.Y - center.Y, (long)center.Y - bounds.Bottom + 1));
                if (horizontal + vertical >= distance) continue;
                nearest = screen;
                distance = horizontal + vertical;
            }

            if (nearest is null) return;
            var area = nearest.WorkingArea;
            var size = IsVisible ? ClientSize : _frame.DesiredSize;
            var scale = IsVisible ? RenderScaling : nearest.Scaling;
            var width = (int)Math.Ceiling(size.Width * scale);
            var height = (int)Math.Ceiling(size.Height * scale);
            var y = pin.Y - height;
            if (y < area.Y) y = pin.Bottom;
            var point = new WindowPixelPoint(Math.Clamp(pin.X, area.X, Math.Max(area.X, area.Right - width)),
                Math.Clamp(y, area.Y, Math.Max(area.Y, area.Bottom - height)));
            if (Position != point) Position = point;
        }
    }
}

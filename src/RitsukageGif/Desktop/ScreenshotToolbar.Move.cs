using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotToolbar
    {
        private static readonly Cursor MoveCursor = new(StandardCursorType.SizeAll);
        private Point? _manualPosition;
        private Point _toolbarDragOrigin;
        private Point? _toolbarDragStart;

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
            var handle = SmallButton("ToolbarMove", () => { }, ToolbarIcon.TryGeometry("ToolbarMove") ??
                                                               ToolbarIcon.Geometry("AnimationSelection"));
            handle.Width = 14;
            handle.Padding = new(2, 0);
            handle.Cursor = MoveCursor;
            handle.GetValue(TooltipBindingProperty)?.Dispose();
            handle.SetValue(TooltipBindingProperty,
                handle.Bind(ToolTip.TipProperty, LiveText.Resource("ToolbarMoveHint")));
            if (handle.Content is Control icon)
            {
                icon.Width = 8;
                icon.Height = 14;
            }

            handle.AddHandler(PointerPressedEvent, (_, args) =>
            {
                args.Handled = true;
                if (IsDocked || !args.GetCurrentPoint(handle).Properties.IsLeftButtonPressed) return;
                if (args.ClickCount == 2)
                {
                    _manualPosition = null;
                    _placedSize = default;
                    Refresh(_placedRegion, _placementMouse, _ownsToolbar);
                    return;
                }

                _toolbarDragStart = args.GetPosition(this.GetVisualParent());
                _toolbarDragOrigin = new(Margin.Left, Margin.Top);
                args.Pointer.Capture(handle);
            }, RoutingStrategies.Tunnel, true);
            handle.AddHandler(PointerMovedEvent, (_, args) =>
            {
                if (_toolbarDragStart is not { } start || !ReferenceEquals(args.Pointer.Captured, handle)) return;
                args.Handled = true;
                var current = args.GetPosition(this.GetVisualParent());
                _manualPosition = _toolbarDragOrigin + (current - start);
                if (TryGetManualPosition(new(_monitor.Width / Scale, _monitor.Height / Scale), Bounds.Size,
                        out var position)) Margin = new(position.X, position.Y, 0, 0);
            }, RoutingStrategies.Tunnel, true);
            handle.AddHandler(PointerReleasedEvent, (_, args) =>
            {
                args.Handled = true;
                _toolbarDragStart = null;
                if (ReferenceEquals(args.Pointer.Captured, handle)) args.Pointer.Capture(null);
            }, RoutingStrategies.Tunnel, true);
            handle.PointerCaptureLost += (_, _) => _toolbarDragStart = null;
            return handle;
        }
    }
}

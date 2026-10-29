using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed class ToolbarDragHandle : Border
    {
        private bool _dragging;
        private Point _origin;
        private bool _pressed;

        public ToolbarDragHandle(Control coordinates, Action<Point> start, Action<Point> move, Action<Point> drop,
            Action cancel)
        {
            InitializeAppearance();
            PointerPressed += (_, args) =>
            {
                if (!args.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
                _origin = args.GetPosition(coordinates);
                _pressed = true;
                args.Pointer.Capture(this);
                args.Handled = true;
            };
            PointerMoved += (_, args) =>
            {
                if (!_pressed) return;
                var point = args.GetPosition(coordinates);
                if (!_dragging && Math.Abs(point.X - _origin.X) + Math.Abs(point.Y - _origin.Y) >= 4)
                {
                    _dragging = true;
                    start(_origin);
                }

                if (_dragging) move(point);
                args.Handled = true;
            };
            PointerReleased += (_, args) =>
            {
                if (!_pressed) return;
                var dragging = _dragging;
                var point = args.GetPosition(coordinates);
                _pressed = false;
                _dragging = false;
                args.Pointer.Capture(null);
                if (dragging) drop(point);
                args.Handled = true;
            };
            PointerCaptureLost += (_, _) =>
            {
                _pressed = false;
                if (!_dragging) return;
                _dragging = false;
                cancel();
            };
        }

        public ToolbarDragHandle(Window window, Action started, Action completed)
        {
            InitializeAppearance();
            PointerPressed += (_, args) =>
            {
                if (!args.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
                args.Handled = true;
                started();
                try
                {
                    window.BeginMoveDrag(args);
                }
                finally
                {
                    completed();
                }
            };
        }

        private void InitializeAppearance()
        {
            Background = Brushes.Transparent;
            Cursor = new(StandardCursorType.SizeAll);
            Width = 18;
            Height = 28;
            Child = new TextBlock
            {
                Text = "⠿", FontSize = 16, Foreground = AppTheme.Muted,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
        }

        public static Rect BoundsIn(Control control, Control coordinates)
        {
            return control.TranslatePoint(default, coordinates) is { } origin
                ? new(origin, control.Bounds.Size)
                : default;
        }

        public static Button Icon(string key)
        {
            var icon = new Button
            {
                Width = 22, Height = 24, MinWidth = 0, MinHeight = 0, Padding = new(0), BorderThickness = new(0),
                Background = Brushes.Transparent, Foreground = AppTheme.Ink, Focusable = false,
                IsHitTestVisible = false,
            };
            if (ToolbarIcon.TryGeometry(key) is { } geometry) icon.Content = ToolbarIcon.Create(icon, geometry);
            return icon;
        }

        public static Button Command(string key, Action execute)
        {
            var button = new Button { Content = UiText.Get(key), MinHeight = 28, Padding = new(10, 4) };
            button.Click += (_, _) => execute();
            return button;
        }
    }
}

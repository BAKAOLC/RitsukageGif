using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed class ScreenshotQrOverlay : Canvas
    {
        private readonly Func<string, Task> _copy;
        private readonly HashSet<int> _dismissed = [];
        private readonly HashSet<int> _expanded = [];
        private readonly Func<Uri, Task> _open;
        private readonly List<(Border Panel, Rect Symbol)> _panels = [];
        private readonly Action<Exception> _reportError;
        private bool _direct;
        private bool _enabled;
        private PixelRect _monitor;
        private Rect[] _occupiedBounds = [];
        private IReadOnlyList<Rect> _reserved = [];
        private double _scale = 1;
        private PixelRect _selection;
        private QrDetectionSnapshot? _snapshot;

        internal ScreenshotQrOverlay(Func<string, Task> copy, Func<Uri, Task> open, Action<Exception> reportError)
        {
            _copy = copy;
            _open = open;
            _reportError = reportError;
            ClipToBounds = true;
            SizeChanged += (_, _) => Rebuild();
        }

        internal bool HasVisiblePopup => _panels.Count > 0;
        internal IReadOnlyList<Rect> OccupiedBounds => _occupiedBounds;
        internal event Action? LayoutChanged;

        internal void Update(QrDetectionSnapshot? snapshot, PixelRect selection, PixelRect monitor, double scale,
            bool enabled, bool direct, bool interactive, IReadOnlyList<Rect> reserved)
        {
            IsHitTestVisible = interactive;
            if (!ReferenceEquals(snapshot, _snapshot))
            {
                _expanded.Clear();
                _dismissed.Clear();
            }
            else if (!_enabled && enabled)
            {
                _dismissed.Clear();
            }

            if (ReferenceEquals(snapshot, _snapshot) && selection == _selection && monitor == _monitor &&
                scale.Equals(_scale) && enabled == _enabled && direct == _direct)
            {
                if (!_reserved.SequenceEqual(reserved))
                {
                    _reserved = reserved;
                    LayoutPopups();
                }

                return;
            }

            _snapshot = snapshot;
            _selection = selection;
            _monitor = monitor;
            _scale = scale;
            _enabled = enabled;
            _direct = direct;
            _reserved = reserved;
            Rebuild();
        }

        private void Rebuild()
        {
            Children.Clear();
            _panels.Clear();
            if (!_enabled || _snapshot is null || _selection.IsEmpty || Bounds.Width <= 0)
            {
                _occupiedBounds = [];
                LayoutChanged?.Invoke();
                return;
            }

            for (var index = 0; index < _snapshot.Codes.Count; index++)
            {
                var code = _snapshot.Codes[index];
                var markers = code.Outline.Count >= 3 ? code.Outline : code.Points;
                if (markers.Count == 0) continue;
                var center = new PixelPoint(_snapshot.Region.X + (int)markers.Average(point => point.X),
                    _snapshot.Region.Y + (int)markers.Average(point => point.Y));
                if (!_selection.Contains(center) || !_monitor.Contains(center)) continue;
                var points = markers.Select(point => new Point(
                    (_snapshot.Region.X + point.X - _monitor.X) / _scale,
                    (_snapshot.Region.Y + point.Y - _monitor.Y) / _scale)).ToArray();
                var symbol = new Rect(points.Min(point => point.X), points.Min(point => point.Y),
                    points.Max(point => point.X) - points.Min(point => point.X),
                    points.Max(point => point.Y) - points.Min(point => point.Y));
                if (code.Outline.Count >= 3)
                    Children.Add(new Polygon
                    {
                        Points = points, Stroke = AppTheme.Accent, StrokeThickness = 1.5, IsHitTestVisible = false,
                    });
                else if (points.Length == 2)
                    Children.Add(new Polyline
                    {
                        Points = points, Stroke = AppTheme.Accent, StrokeThickness = 1.5, IsHitTestVisible = false,
                    });
                else
                    foreach (var point in points)
                    {
                        var marker = new Ellipse
                            { Width = 5, Height = 5, Fill = AppTheme.Accent, IsHitTestVisible = false };
                        SetLeft(marker, point.X - 2.5);
                        SetTop(marker, point.Y - 2.5);
                        Children.Add(marker);
                    }

                var item = index;
                if ((_direct && !_dismissed.Contains(index)) || _expanded.Contains(index))
                {
                    var panel = Popup(code.Text, item);
                    Children.Add(panel);
                    _panels.Add((panel, symbol));
                }
                else
                {
                    var button = IconButton("RecognizeBarcode", () =>
                    {
                        _expanded.Add(item);
                        Rebuild();
                    });
                    SetLeft(button, symbol.Center.X - 14);
                    SetTop(button, symbol.Center.Y - 14);
                    Children.Add(button);
                }
            }

            LayoutPopups();
        }

        private void LayoutPopups()
        {
            var occupied = _reserved.ToList();
            foreach (var (panel, symbol) in _panels)
            {
                panel.Measure(new(Math.Min(320, Bounds.Width - 16), Math.Min(240, Bounds.Height - 16)));
                var frame = QrPopupPlacement.Place(symbol, panel.DesiredSize, Bounds.Size, occupied);
                panel.Width = frame.Width;
                panel.Height = frame.Height;
                SetLeft(panel, frame.X);
                SetTop(panel, frame.Y);
                occupied.Add(frame);
            }

            _occupiedBounds =
            [
                .. _panels.Select(item => new Rect(GetLeft(item.Panel), GetTop(item.Panel),
                    item.Panel.Width, item.Panel.Height)),
            ];
            LayoutChanged?.Invoke();
        }

        private Border Popup(string text, int item)
        {
            var body = new TextBox
            {
                Text = text, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                MinHeight = 32, MaxHeight = 148, FontSize = 13, Padding = new(6),
            };
            body.KeyDown += (_, args) =>
            {
                if (args.Key != Key.Escape) return;
                Dismiss(item);
                args.Handled = true;
            };
            var title = new TextBlock
                { Text = UiText.Get("QrCodeTitle"), VerticalAlignment = VerticalAlignment.Center };
            var close = IconButton("Cancel", () => Dismiss(item), "Close");
            var header = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 4 };
            header.Children.Add(title);
            Grid.SetColumn(close, 1);
            header.Children.Add(close);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            var copy = new Button
                { Content = UiText.Get("QrCodeCopy"), Padding = new(8, 3), MinHeight = 0, Focusable = false };
            copy.Click += async (_, _) => await RunAsync(copy,
                () => _copy(string.IsNullOrEmpty(body.SelectedText) ? text : body.SelectedText));
            actions.Children.Add(copy);
            if (Uri.TryCreate(text.Trim(), UriKind.Absolute, out var link))
            {
                var open = new Button
                    { Content = UiText.Get("QrCodeOpenLink"), Padding = new(8, 3), MinHeight = 0, Focusable = false };
                open.Click += async (_, _) => await RunAsync(open, () => _open(link));
                actions.Children.Add(open);
            }

            var content = new Grid { RowDefinitions = new("Auto,*,Auto"), RowSpacing = 5 };
            content.Children.Add(header);
            Grid.SetRow(body, 1);
            content.Children.Add(body);
            Grid.SetRow(actions, 2);
            content.Children.Add(actions);
            return new()
            {
                Width = 288, MaxHeight = 232, Padding = new(8), Background = AppTheme.Surface,
                BorderBrush = AppTheme.Outline, BorderThickness = new(1), CornerRadius = new(5), Child = content,
            };
        }

        private void Dismiss(int item)
        {
            _expanded.Remove(item);
            _dismissed.Add(item);
            Rebuild();
        }

        private static Button IconButton(string key, Action action, string? label = null)
        {
            var button = new Button
                { Width = 28, Height = 28, MinWidth = 0, MinHeight = 0, Padding = new(2), Focusable = false };
            button.Content = ToolbarIcon.Create(button, ToolbarIcon.FindDefinition(key)!);
            button.SetValue(AutomationProperties.NameProperty, UiText.Get(label ?? key));
            ToolTip.SetTip(button, UiText.Get(label ?? key));
            button.Click += (_, _) => action();
            return button;
        }

        private async Task RunAsync(Control control, Func<Task> operation)
        {
            control.IsEnabled = false;
            try
            {
                await operation();
            }
            catch (Exception error)
            {
                _reportError(error);
                ToolTip.SetTip(control, error.Message);
            }
            finally
            {
                control.IsEnabled = true;
            }
        }
    }
}

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed class SelectionModePicker : Canvas, IDisposable
    {
        private readonly Button[] _buttons;
        private readonly SelectionCoordinator _coordinator;

        private readonly ProgressBar _countdown = new()
        {
            Minimum = 0, Maximum = 100, Height = 4, MinHeight = 0,
            Background = AppTheme.Outline, Foreground = AppTheme.Accent, IsVisible = false,
        };

        private readonly StackPanel _row = new()
        {
            Orientation = Orientation.Horizontal, Spacing = 24, RenderTransformOrigin = RelativePoint.TopLeft,
        };

        private IDisposable? _countdownTick;
        private SelectionShapeMode? _displayed;
        private double _fitScale;

        internal SelectionModePicker(SelectionCoordinator coordinator)
        {
            _coordinator = coordinator;
            _buttons =
            [
                Create(SelectionShapeMode.Rectangle, "SelectionModeRectangle",
                    ToolbarIcon.GetDefinition("SelectionModeRectangle")),
                Create(SelectionShapeMode.Free, "SelectionModeFree",
                    ToolbarIcon.GetDefinition("SelectionModeFree")),
                Create(SelectionShapeMode.Polygon, "SelectionModePolygon",
                    ToolbarIcon.GetDefinition("SelectionModePolygon")),
                Create(SelectionShapeMode.MultiWindow, "SelectionModeMultiWindow",
                    ToolbarIcon.GetDefinition("SelectionModeMultiWindow")),
            ];
            foreach (var button in _buttons) _row.Children.Add(button);
            Children.Add(_row);
            Children.Add(_countdown);
            IsVisible = false;
        }

        internal Rect OccupiedBounds { get; private set; }

        public void Dispose()
        {
            _countdownTick?.Dispose();
            _countdownTick = null;
        }

        internal void Update(PixelRect monitor, double scale, PixelPoint pointer)
        {
            IsVisible = _coordinator.ShapeModePreviewVisible && monitor.Contains(pointer);
            if (!IsVisible)
            {
                OccupiedBounds = default;
                Dispose();
                return;
            }

            if (_displayed != _coordinator.DisplayShapeMode)
            {
                _displayed = _coordinator.DisplayShapeMode;
                for (var index = 0; index < _buttons.Length; index++)
                {
                    _buttons[index].Background = _displayed == (SelectionShapeMode)index
                        ? AppTheme.SurfaceTint
                        : AppTheme.Surface;
                    _buttons[index].BorderBrush =
                        _displayed == (SelectionShapeMode)index ? AppTheme.Accent : AppTheme.Outline;
                }
            }

            var viewport = new Size(monitor.Width / scale, monitor.Height / scale);
            var fit = Math.Min(1, Math.Min(viewport.Width / 472, viewport.Height / 116));
            if (!_fitScale.Equals(fit))
            {
                _fitScale = fit;
                _row.RenderTransform = new ScaleTransform(fit, fit);
            }

            var width = 472 * fit;
            var rowHeight = 100 * fit;
            var countdownGap = 12 * fit;
            var countdownHeight = Math.Max(2, 4 * fit);
            var x = Math.Clamp((viewport.Width - width) / 2, 0, Math.Max(0, viewport.Width - width));
            var y = Math.Clamp(viewport.Height * .9 - rowHeight / 2, 0,
                Math.Max(0, viewport.Height - rowHeight - countdownGap - countdownHeight));
            SetLeft(_row, x);
            SetTop(_row, y);
            SetLeft(_countdown, x);
            SetTop(_countdown, y + rowHeight + countdownGap);
            _countdown.Width = width;
            _countdown.Height = countdownHeight;
            _countdown.IsVisible = _coordinator.ShapeModeCountdownVisible;
            OccupiedBounds = new(x, y, width,
                _countdown.IsVisible ? rowHeight + countdownGap + countdownHeight : rowHeight);
            UpdateCountdown();
        }

        private void UpdateCountdown()
        {
            _countdownTick?.Dispose();
            _countdownTick = null;
            if (!IsVisible || !_countdown.IsVisible) return;
            _countdown.Value = _coordinator.ShapeModeCountdownProgress * 100;
            _countdownTick = DispatcherTimer.RunOnce(UpdateCountdown, TimeSpan.FromMilliseconds(33));
        }

        private Button Create(SelectionShapeMode mode, string label, IconDefinition path)
        {
            var button = new Button
            {
                Width = 100, Height = 100, MinWidth = 0, MinHeight = 0, Padding = new(0), CornerRadius = new(8),
                Focusable = false, IsTabStop = false, Foreground = AppTheme.Ink, BorderThickness = new(1),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                Cursor = new(StandardCursorType.Hand),
            };
            var icon = ToolbarIcon.Create(button, path);
            icon.Width = icon.Height = 40;
            SetLeft(icon, 30);
            SetTop(icon, 11);
            var text = new TextBlock
            {
                Width = 92, FontSize = 10, TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
            };
            text.Bind(TextBlock.TextProperty, LiveText.Resource(label));
            text.Bind(TextBlock.ForegroundProperty, button.GetObservable(TemplatedControl.ForegroundProperty));
            var labelFrame = new Border { Width = 92, Height = 34, Child = text };
            SetLeft(labelFrame, 4);
            SetTop(labelFrame, 55);
            button.Content = new Canvas { Children = { icon, labelFrame } };
            button.Bind(AutomationProperties.NameProperty, LiveText.Resource(label));
            button.Click += (_, _) => _coordinator.SetShapeMode(mode);
            return button;
        }
    }
}

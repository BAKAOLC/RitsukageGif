using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SelectionFrameControls : Canvas, IDisposable
    {
        private readonly StackPanel _buttons = new() { Spacing = 4 };
        private readonly SelectionCoordinator _coordinator;
        private readonly ToggleButton _decoration;
        private readonly Border _popup;
        private readonly ToggleButton _ratio;
        private readonly Button _refresh;
        private readonly ToggleButton _round;
        private Rect _buttonsBounds;
        private IDisposable? _closePopup;
        private bool _disposed;
        private Rect _popupBounds;
        private Rect _toolbarBounds, _optionsBounds;
        private bool _updating;
        private Size _viewport;

        internal SelectionFrameControls(SelectionCoordinator coordinator)
        {
            _coordinator = coordinator;
            _round = Toggle("SelectionFrameRounded", "M5,20 L5,10 Q5,5 10,5 L20,5 M5,14 L5,20 11,20",
                coordinator.SetFrameRound);
            _ratio = Toggle("SelectionFrameRatio", "M4,10 L4,4 10,4 M4,4 L10,10 M14,20 L20,20 20,14 M20,20 L14,14",
                coordinator.LockGeometryRatio);
            _decoration = Toggle("SelectionFrameDecoration",
                "M12,3 A9,9 0 1 0 12,21 Z M12,3 L12,21 M12,7 L18,7 M12,11 L20,11 M12,15 L19,15",
                coordinator.SetFrameDecoration);
            _refresh = new()
            {
                Width = 32, Height = 32, MinWidth = 0, MinHeight = 0, Padding = new(7), CornerRadius = new(16),
                Focusable = false, IsTabStop = false, Background = AppTheme.Surface, Foreground = AppTheme.Ink,
                BorderThickness = new(0),
            };
            _refresh.Content = Icon(_refresh,
                "M20,10 A8,8 0 0 0 5,6 M4,2 L4,7 9,7 M4,14 A8,8 0 0 0 19,18 M20,22 L20,17 15,17");
            _refresh.Bind(ToolTip.TipProperty, LiveText.Resource("SelectionFrameRefresh"));
            _refresh.Bind(AutomationProperties.NameProperty, LiveText.Resource("SelectionFrameRefresh"));
            _refresh.AddHandler(PointerPressedEvent, (_, args) =>
            {
                if (!args.GetCurrentPoint(_refresh).Properties.IsLeftButtonPressed) return;
                ClosePopup();
                args.Handled = true;
                coordinator.StartFrameRefresh();
            }, RoutingStrategies.Tunnel);
            _refresh.PointerReleased += (_, _) => coordinator.StopFrameRefresh();
            _buttons.Children.Add(_refresh);
            Children.Add(_buttons);
            var roundPanel = CreateRoundPanel();
            var decorationPanel = CreateDecorationPanel();
            _popup = new()
            {
                Background = AppTheme.Surface, BorderBrush = AppTheme.Outline,
                BorderThickness = new(1), CornerRadius = new(6), Padding = new(10), Width = 256, IsVisible = false,
            };
            _popup.PointerEntered += (_, _) => CancelClose();
            _popup.PointerExited += (_, _) => ScheduleClose();
            Children.Add(_popup);
            _round.PointerEntered += (_, _) => ShowPopup(roundPanel);
            _decoration.PointerEntered += (_, _) => ShowPopup(decorationPanel);
            _ratio.PointerEntered += (_, _) => ClosePopup();
            _refresh.PointerEntered += (_, _) => ClosePopup();
            _round.PointerExited += (_, _) => ScheduleClose();
            _decoration.PointerExited += (_, _) => ScheduleClose();
        }

        internal Rect OccupiedBounds => !IsVisible
            ? default
            : _popup.IsVisible
                ? _buttonsBounds.Union(_popupBounds)
                : _buttonsBounds;

        public void Dispose()
        {
            _disposed = true;
            CancelClose();
            _coordinator.StopFrameRefresh();
        }

        internal event Action? LayoutChanged;

        internal void Update(PixelRect monitor, double scale, PixelRect selected, bool owner, Rect toolbar,
            Rect options)
        {
            IsVisible = owner && _coordinator.CanShowFrameControls;
            if (!IsVisible)
            {
                ClosePopup();
                return;
            }

            _updating = true;
            try
            {
                _round.IsChecked = _coordinator.FrameRoundEnabled;
                _ratio.IsChecked = _coordinator.GeometryRatioLocked;
                _decoration.IsChecked = _coordinator.FrameDecorationEnabled;
                _round.Background = _round.IsChecked == true ? AppTheme.Accent : AppTheme.Surface;
                _ratio.Background = _ratio.IsChecked == true ? AppTheme.Accent : AppTheme.Surface;
                _decoration.Background = _decoration.IsChecked == true ? AppTheme.Accent : AppTheme.Surface;
                _round.Foreground = _round.IsChecked == true ? Brushes.White : AppTheme.Ink;
                _ratio.Foreground = _ratio.IsChecked == true ? Brushes.White : AppTheme.Ink;
                _decoration.Foreground = _decoration.IsChecked == true ? Brushes.White : AppTheme.Ink;
                _refresh.IsEnabled = _coordinator.CanRefreshFrame;
                UpdateProperties();
            }
            finally
            {
                _updating = false;
            }

            _viewport = new(monitor.Width / scale, monitor.Height / scale);
            _toolbarBounds = toolbar;
            _optionsBounds = options;
            var region = new Rect((selected.X - monitor.X) / scale, (selected.Y - monitor.Y) / scale,
                selected.Width / scale, selected.Height / scale);
            _buttonsBounds = SelectionFrameLayout.Place(_viewport, region, new(32, 140), toolbar, options);
            SetLeft(_buttons, _buttonsBounds.X);
            SetTop(_buttons, _buttonsBounds.Y);
            PlacePopup();
        }

        internal void ClosePopup()
        {
            CancelClose();
            var visible = _popup.IsVisible;
            _popup.IsVisible = false;
            _popupBounds = default;
            if (visible) LayoutChanged?.Invoke();
        }

        private ToggleButton Toggle(string label, string path, Action<bool> changed)
        {
            var button = new ToggleButton
            {
                Width = 32, Height = 32, MinWidth = 0, MinHeight = 0, Padding = new(7), CornerRadius = new(16),
                Focusable = false, IsTabStop = false, Background = AppTheme.Surface, Foreground = AppTheme.Ink,
                BorderThickness = new(0),
            };
            button.Content = Icon(button, path);
            button.Bind(ToolTip.TipProperty, LiveText.Resource(label));
            button.Bind(AutomationProperties.NameProperty, LiveText.Resource(label));
            button.Click += (_, _) =>
            {
                if (!_updating) changed(button.IsChecked == true);
            };
            _buttons.Children.Add(button);
            return button;
        }

        private static Control Icon(Button button, string path)
        {
            var icon = ToolbarIcon.Create(button, path);
            icon.Width = icon.Height = 18;
            return icon;
        }

        private void ShowPopup(Control content)
        {
            CancelClose();
            _popup.Child = content;
            _popup.IsVisible = true;
            PlacePopup();
            LayoutChanged?.Invoke();
        }

        private void PlacePopup()
        {
            if (!_popup.IsVisible) return;
            _popup.Measure(new(256, _viewport.Height));
            var height = _popup.DesiredSize.Height;
            _popupBounds = SelectionFrameLayout.Place(_viewport, _buttonsBounds, new(256, height), _toolbarBounds,
                _optionsBounds);
            SetLeft(_popup, _popupBounds.X);
            SetTop(_popup, _popupBounds.Y);
        }

        private void ScheduleClose()
        {
            CancelClose();
            _closePopup = DispatcherTimer.RunOnce(() =>
            {
                _closePopup = null;
                if (!_disposed && !_popup.IsPointerOver && !_round.IsPointerOver && !_decoration.IsPointerOver)
                    ClosePopup();
            }, TimeSpan.FromMilliseconds(200));
        }

        private void CancelClose()
        {
            _closePopup?.Dispose();
            _closePopup = null;
        }
    }
}

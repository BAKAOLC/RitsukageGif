using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia_PixelPoint = Avalonia.PixelPoint;

namespace RitsukageGif.Desktop
{
    internal sealed partial class DesktopToolbarWindow : Window
    {
        private readonly List<(Control Control, string Id)> _actionControls = [];
        private readonly Action _adjust;
        private readonly bool _administratorDropRestricted;
        private readonly Bitmap _ballImage;
        private readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal, Spacing = 2 };
        private readonly Action<bool> _close;
        private readonly Action<string> _execute;
        private readonly Func<IReadOnlyList<string>, Task> _pinFiles;
        private readonly Action<string> _pinText;
        private readonly Action _settings;
        private bool _ballDragging;
        private PointerPressedEventArgs? _ballPress;
        private Point _ballPressPosition;
        private bool _positionInitialized;
        private DesktopToolbarPreferences _preferences = new();

        internal DesktopToolbarWindow(Action<string> execute, Action settings, Action adjust, Action<bool> close,
            Action<PixelPoint> positionChanged, Func<IReadOnlyList<string>, Task> pinFiles, Action<string> pinText)
        {
            _execute = execute;
            _settings = settings;
            _adjust = adjust;
            _close = close;
            _pinFiles = pinFiles;
            _pinText = pinText;
            _administratorDropRestricted = StartupRegistrationService.IsRunningAsAdministrator();
            Title = "RitsukageGif";
            WindowDecorations = WindowDecorations.None;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            CanResize = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            Padding = new(4);
            using var iconStream = AssetLoader.Open(new("avares://RitsukageGif/!__AvaloniaDefaultWindowIcon"));
            Icon = new(iconStream);
            using var ballStream = AssetLoader.Open(new("avares://RitsukageGif/!__AvaloniaDefaultWindowIcon"));
            _ballImage = new(ballStream);
            _ballPopupTimer.Tick += (_, _) => CloseBallPopup();
            Closed += (_, _) =>
            {
                CloseBallPopup();
                _ballImage.Dispose();
            };
            PropertyChanged += (_, args) =>
            {
                if (args.Property == IsVisibleProperty && !IsVisible) CloseBallPopup();
            };
            Content = _buttons;
            DragDrop.SetAllowDrop(this, true);
            AddHandler(DragDrop.DragOverEvent, DragOver);
            AddHandler(DragDrop.DropEvent, Drop);
            PositionChanged += (_, _) =>
            {
                if (_positionInitialized) positionChanged(new(Position.X, Position.Y));
            };
            Opened += (_, _) =>
            {
                ConfigureNativeWindow();
                ClampPosition();
                _positionInitialized = true;
            };
        }

        internal Action<DesktopToolbarMode>? ModeChanged { private get; init; }

        internal void Apply(DesktopToolbarPreferences preferences, Func<string, bool> canExecute)
        {
            _preferences = preferences;
            CloseBallPopup();
            _ballPopup = null;
            _buttons.Children.Clear();
            _actionControls.Clear();
            if (preferences.Mode == DesktopToolbarMode.FloatingBall)
            {
                _buttons.Children.Add(BuildFloatingBall(preferences, canExecute));
            }
            else
            {
                var move = IconButton("DesktopToolbarMove", "ToolbarMove");
                ToolTip.SetTip(move, DropTooltip("DesktopToolbarMove"));
                move.AddHandler(PointerPressedEvent, (_, args) =>
                {
                    if (args.GetCurrentPoint(move).Properties.IsLeftButtonPressed) BeginMoveDrag(args);
                }, RoutingStrategies.Bubble, true);
                _buttons.Children.Add(move);
                foreach (var id in preferences.Primary)
                {
                    var button = IconButton(DesktopToolbarActions.NameKey(id),
                        DesktopToolbarActions.Definition(id).IconKey);
                    button.IsEnabled = canExecute(id);
                    button.Click += (_, _) => _execute(id);
                    _actionControls.Add((button, id));
                    _buttons.Children.Add(button);
                }

                var more = IconButton("DesktopToolbarMore", "ScreenshotMore");
                more.Flyout = new MenuFlyout { ItemsSource = BuildMenu(canExecute) };
                _buttons.Children.Add(more);
            }

            ContextMenu = new() { ItemsSource = BuildMenu(canExecute) };
            if (preferences is { Left: { } x, Top: { } y } && Position != new Avalonia_PixelPoint(x, y))
                Position = new(x, y);
            RefreshAvailability(canExecute);
        }

        internal void RefreshAvailability(Func<string, bool> canExecute)
        {
            foreach (var (control, id) in _actionControls) control.IsEnabled = canExecute(id);
        }

        private List<Control> BuildMenu(Func<string, bool> canExecute)
        {
            var items = new List<Control>();
            var actions = _preferences.Mode == DesktopToolbarMode.FloatingBall
                ? _preferences.Primary.Concat(_preferences.More)
                : _preferences.More;
            foreach (var id in actions)
            {
                var item = new MenuItem
                    { Header = UiText.Get(DesktopToolbarActions.NameKey(id)), IsEnabled = canExecute(id) };
                item.Click += (_, _) => _execute(id);
                _actionControls.Add((item, id));
                items.Add(item);
            }

            if (items.Count > 0) items.Add(new Separator());
            items.Add(ModeMenuItem());
            AddMenu(items, "DesktopToolbarAdjust", _adjust);
            AddMenu(items, "Settings", _settings);
            AddMenu(items, "DesktopToolbarHideToday", () => _close(true));
            AddMenu(items, "DesktopToolbarClose", () => _close(false));
            return items;
        }

        private MenuItem ModeMenuItem()
        {
            var mode = _preferences.Mode == DesktopToolbarMode.FloatingBall
                ? DesktopToolbarMode.Toolbar
                : DesktopToolbarMode.FloatingBall;
            var item = new MenuItem
            {
                Header = UiText.Get(mode == DesktopToolbarMode.Toolbar
                    ? "SystemDesktopToolbar"
                    : "SystemDesktopFloatingBall"),
                IsEnabled = ModeChanged is not null,
            };
            item.Click += (_, _) => ModeChanged?.Invoke(mode);
            return item;
        }

        private static void AddMenu(List<Control> items, string name, Action action)
        {
            var item = new MenuItem { Header = UiText.Get(name) };
            item.Click += (_, _) => action();
            items.Add(item);
        }

        private static Button IconButton(string name, string iconKey)
        {
            var button = new Button { Width = 30, Height = 30, Padding = new(6) };
            button.Content =
                ToolbarIcon.Create(button, ToolbarIcon.TryGeometry(iconKey) ?? ToolbarIcon.Geometry(iconKey));
            ToolTip.SetTip(button, UiText.Get(name));
            return button;
        }

        private string DropTooltip(string name)
        {
            var text = UiText.Get(name);
            return _administratorDropRestricted
                ? text + Environment.NewLine + UiText.Get("DesktopToolbarAdministratorDrop")
                : text;
        }

        internal void ClampPosition()
        {
            var area = (Screens.ScreenFromPoint(Position) ?? Screens.Primary)?.WorkingArea;
            if (area is not { } work) return;
            Position = new(
                Math.Clamp(Position.X, work.X, Math.Max(work.X, work.Right - (int)(Bounds.Width * RenderScaling))),
                Math.Clamp(Position.Y, work.Y, Math.Max(work.Y, work.Bottom - (int)(Bounds.Height * RenderScaling))));
        }

        private static void DragOver(object? sender, DragEventArgs args)
        {
            args.DragEffects = args.DataTransfer.Contains(DataFormat.File) ||
                               args.DataTransfer.Contains(DataFormat.Text)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            args.Handled = true;
        }

        // ReSharper disable once AsyncVoidEventHandlerMethod
        private async void Drop(object? sender, DragEventArgs args)
        {
            args.Handled = true;
            try
            {
                if (args.DataTransfer.TryGetFiles() is { } files)
                    await _pinFiles([.. files.Select(file => file.TryGetLocalPath()).OfType<string>()]);
                else if (args.DataTransfer.TryGetText() is { } text) _pinText(text);
            }
            catch (Exception error)
            {
                LogHub.GetLogger("Desktop.Toolbar").ZLogError(error, $"Desktop toolbar drop failed.");
            }
        }
    }
}

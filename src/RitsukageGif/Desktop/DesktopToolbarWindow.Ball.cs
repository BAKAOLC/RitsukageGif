using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class DesktopToolbarWindow
    {
        private readonly DispatcherTimer _ballPopupTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
        private bool _ballClickSuppressed;
        private Popup? _ballPopup;

        private Grid BuildFloatingBall(DesktopToolbarPreferences preferences, Func<string, bool> canExecute)
        {
            CloseBallPopup();
            var ball = new Button
            {
                Width = 36, Height = 36, Padding = new(4),
                Content = new Image { Source = _ballImage, Width = 24, Height = 24 },
            };
            var ids = preferences.Primary.Concat(preferences.More).ToArray();
            var ballId = ids.Contains("screenshot") ? "screenshot" : ids.FirstOrDefault();
            ball.Click += (_, _) =>
            {
                if (!_ballClickSuppressed && ballId is not null) _execute(ballId);
            };
            if (ballId is not null)
            {
                _actionControls.Add((ball, ballId));
                ToolTip.SetTip(ball, DropTooltip(DesktopToolbarActions.NameKey(ballId)));
            }
            else
            {
                ball.IsEnabled = false;
            }

            ball.AddHandler(PointerPressedEvent, (_, args) =>
            {
                if (!args.GetCurrentPoint(ball).Properties.IsLeftButtonPressed) return;
                _ballPress = args;
                _ballPressPosition = args.GetPosition(this);
                _ballDragging = false;
                _ballClickSuppressed = false;
            }, RoutingStrategies.Bubble, true);
            ball.AddHandler(PointerMovedEvent, (_, args) =>
            {
                if (_ballPress is null || _ballDragging ||
                    !args.GetCurrentPoint(ball).Properties.IsLeftButtonPressed) return;
                var delta = args.GetPosition(this) - _ballPressPosition;
                if (Math.Abs(delta.X) + Math.Abs(delta.Y) <= 5) return;
                _ballDragging = true;
                _ballClickSuppressed = true;
                CloseBallPopup();
                BeginMoveDrag(_ballPress);
            }, RoutingStrategies.Bubble, true);
            ball.AddHandler(PointerReleasedEvent, (_, _) =>
            {
                _ballPress = null;
                _ballDragging = false;
            }, RoutingStrategies.Bubble, true);
            ball.PointerCaptureLost += (_, _) =>
            {
                _ballPress = null;
                _ballDragging = false;
            };
            var close = IconButton("DesktopToolbarClose", "Cancel");
            close.Width = close.Height = 14;
            close.MinWidth = close.MinHeight = 0;
            close.Padding = new(1);
            close.Content = new Viewbox
            {
                Width = 10, Height = 10,
                Child = ToolbarIcon.Create(close, ToolbarIcon.FindDefinition("Cancel")!),
            };
            close.HorizontalAlignment = HorizontalAlignment.Right;
            close.VerticalAlignment = VerticalAlignment.Top;
            close.IsVisible = false;
            var hideToday = new MenuItem { Header = UiText.Get("DesktopToolbarHideToday") };
            hideToday.Click += (_, _) => _close(true);
            var disable = new MenuItem { Header = UiText.Get("DesktopToolbarClose") };
            disable.Click += (_, _) => _close(false);
            close.Flyout = new MenuFlyout
                { ItemsSource = new Control[] { hideToday, disable, new Separator(), ModeMenuItem() } };
            var container = new Grid { Width = 36, Height = 36 };
            container.Children.Add(ball);
            container.Children.Add(close);
            var commands = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
            foreach (var id in preferences.Primary)
            {
                var button = IconButton(DesktopToolbarActions.NameKey(id),
                    DesktopToolbarActions.Definition(id).IconKey);
                button.Click += (_, _) => _execute(id);
                _actionControls.Add((button, id));
                commands.Children.Add(button);
            }

            var more = IconButton("DesktopToolbarMore", "ScreenshotMore");
            more.Flyout = new MenuFlyout { ItemsSource = BuildMenu(canExecute) };
            commands.Children.Add(more);
            var panel = new Border
            {
                Child = commands, Padding = new(4), CornerRadius = new(6), Background = AppTheme.Surface,
                BorderBrush = AppTheme.Outline, BorderThickness = new(1),
            };
            var popup = new Popup
            {
                PlacementTarget = container, Placement = PlacementMode.RightEdgeAlignedTop,
                IsLightDismissEnabled = false, Child = panel,
            };
            _ballPopup = popup;
            container.Children.Add(popup);
            _ballPopupTimer.Stop();
            container.PointerEntered += (_, _) =>
            {
                _ballPopupTimer.Stop();
                close.IsVisible = true;
                if (!_ballDragging) popup.IsOpen = true;
            };
            container.PointerExited += (_, _) =>
            {
                close.IsVisible = false;
                _ballPopupTimer.Start();
            };
            container.PointerMoved += (_, args) =>
            {
                if (IsVisible && !_ballDragging && !args.GetCurrentPoint(container).Properties.IsLeftButtonPressed)
                    popup.IsOpen = true;
            };
            panel.PointerEntered += (_, _) => _ballPopupTimer.Stop();
            panel.PointerExited += (_, _) => _ballPopupTimer.Start();
            return container;
        }

        private void CloseBallPopup()
        {
            _ballPopupTimer.Stop();
            if (_ballPopup is { } popup) popup.IsOpen = false;
        }
    }
}

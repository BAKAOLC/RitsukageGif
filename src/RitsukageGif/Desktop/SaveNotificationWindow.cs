using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SaveNotificationWindow : Window
    {
        private readonly TextBlock _message;
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };
        private string? _path;
        private Screen? _screen;

        internal SaveNotificationWindow(Action<string> error)
        {
            Width = 360;
            Height = 84;
            CanResize = false;
            TransientWindowShell.Configure(this);
            ShowActivated = false;
            Focusable = false;
            Topmost = true;
            SystemDecorations = SystemDecorations.None;
            Background = AppTheme.Surface;
            _message = new()
            {
                Foreground = AppTheme.Ink, TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 3,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var open = new Button
            {
                Content = _message, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Center, Padding = new(12, 8),
                Background = Brushes.Transparent, BorderThickness = new(0), Focusable = false,
            };
            ToolTip.SetTip(open, UiText.Get("OpenFolder"));
            open.Click += (_, _) =>
            {
                if (_path is not { } path) return;
                try
                {
                    PlatformShell.RevealFile(path);
                }
                catch (Exception exception)
                {
                    error(exception.Message);
                }

                Close();
            };
            var close = new Button
            {
                Content = "×", Width = 26, Height = 26, MinWidth = 0, MinHeight = 0,
                Padding = new(0), Focusable = false, VerticalAlignment = VerticalAlignment.Top,
                Background = Brushes.Transparent, BorderThickness = new(0),
            };
            ToolTip.SetTip(close, UiText.Get("Close"));
            close.Click += (_, _) => Close();
            var row = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new(2) };
            row.Children.Add(open);
            Grid.SetColumn(close, 1);
            row.Children.Add(close);
            Content = new Border
            {
                BorderBrush = AppTheme.Outline, BorderThickness = new(1), Child = row,
            };
            _timer.Tick += OnTimeout;
            Closed += (_, _) =>
            {
                _timer.Stop();
                _timer.Tick -= OnTimeout;
            };
            Opened += (_, _) =>
            {
                ConfigureNativeWindow();
                Place();
            };
            ConfigureNativeWindow();
        }

        internal void Present(IReadOnlyList<string> paths, Screen? screen)
        {
            if (paths.Count == 0) return;
            _path = paths[0];
            _screen = screen ?? Screens.Primary;
            _message.Text = UiText.Format("AnimationExported", string.Join(", ", paths.Select(Path.GetFileName)));
            _timer.Stop();
            Place();
            if (!IsVisible) Show();
            _timer.Start();
        }

        private void Place()
        {
            if (_screen is not { } screen) return;
            var area = screen.WorkingArea;
            var scale = screen.Scaling;
            Width = Math.Min(360, Math.Max(160, area.Width / scale - 24));
            Position = new(
                area.Right - (int)Math.Ceiling((Width + 12) * scale),
                area.Bottom - (int)Math.Ceiling((Height + 12) * scale));
        }

        private void OnTimeout(object? sender, EventArgs args)
        {
            Close();
        }
    }
}

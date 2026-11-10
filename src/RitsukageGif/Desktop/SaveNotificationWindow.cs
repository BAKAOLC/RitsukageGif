using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SaveNotificationWindow : Window
    {
        private readonly SelectableTextBlock _message;
        private readonly Button _open;
        // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
        private readonly Bitmap _icon;
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(4) };
        private string[] _paths = [];
        private SaveNotificationAction _action;
        private Screen? _screen;

        internal SaveNotificationWindow(Action<string> error, Func<IReadOnlyList<string>, Task> copyFiles)
        {
            Width = 360;
            SizeToContent = SizeToContent.Height;
            MinHeight = 120;
            MaxHeight = 240;
            CanResize = false;
            TransientWindowShell.Configure(this);
            ShowActivated = false;
            Focusable = false;
            Topmost = true;
            WindowDecorations = WindowDecorations.None;
            Background = AppTheme.Surface;
            _message = new()
            {
                Foreground = AppTheme.Ink, TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 6,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _open = new()
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                Padding = new(12, 5), Focusable = false,
            };
            _open.Click += async (_, _) =>
            {
                var paths = _paths;
                if (paths.Length == 0) return;
                try
                {
                    switch (_action)
                    {
                        case SaveNotificationAction.LocateFile: await PlatformShell.RevealFileAsync(paths[0]); break;
                        case SaveNotificationAction.CopyFile: await copyFiles(paths); break;
                        case SaveNotificationAction.CopyPath:
                            var clipboard = Clipboard ??
                                            throw new InvalidOperationException(UiText.Get("ClipboardFailed"));
                            await clipboard.SetTextAsync(string.Join(Environment.NewLine, paths));
                            await clipboard.FlushAsync();
                            break;
                        default: throw new InvalidOperationException("Unsupported save notification action.");
                    }
                }
                catch (Exception exception)
                {
                    error(exception.Message);
                }

                Close();
            };
            var close = new Button
            {
                Width = 26, Height = 26, MinWidth = 0, MinHeight = 0,
                Padding = new(0), Focusable = false, VerticalAlignment = VerticalAlignment.Top,
                Background = Brushes.Transparent, BorderThickness = new(0),
            };
            close.Content = ToolbarIcon.Create(close, ToolbarIcon.FindDefinition("Cancel")!);
            ToolTip.SetTip(close, UiText.Get("Close"));
            close.Click += (_, _) => Close();
            using var iconStream = AssetLoader.Open(new("avares://RitsukageGif/!__AvaloniaDefaultWindowIcon"));
            _icon = new(iconStream);
            var title = new TextBlock
                { FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            title.Bind(TextBlock.TextProperty, LiveText.Resource("CaptureActionSaveImage"));
            var header = new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = 8 };
            header.Children.Add(new Image { Source = _icon, Width = 22, Height = 22 });
            Grid.SetColumn(title, 1);
            header.Children.Add(title);
            Grid.SetColumn(close, 2);
            header.Children.Add(close);
            var row = new Grid { RowDefinitions = new("Auto,Auto,Auto"), RowSpacing = 8, Margin = new(12) };
            row.Children.Add(header);
            Grid.SetRow(_message, 1);
            row.Children.Add(_message);
            Grid.SetRow(_open, 2);
            row.Children.Add(_open);
            Content = new Border
            {
                BorderBrush = AppTheme.Outline, BorderThickness = new(1), Child = row,
            };
            _timer.Tick += OnTimeout;
            PointerEntered += (_, _) => _timer.Stop();
            PointerExited += (_, _) =>
            {
                if (!IsPointerOver) _timer.Start();
            };
            Closed += (_, _) =>
            {
                _timer.Stop();
                _timer.Tick -= OnTimeout;
                _icon.Dispose();
            };
            Opened += (_, _) =>
            {
                ConfigureNativeWindow();
                Place();
            };
            SizeChanged += (_, _) => Place();
            ConfigureNativeWindow();
        }

        internal void Present(IReadOnlyList<string> paths, Screen? screen, SaveNotificationAction action)
        {
            if (paths.Count == 0) return;
            _paths = [.. paths];
            _action = action;
            _open.Content = UiText.Get(action switch
            {
                SaveNotificationAction.LocateFile => "ScreenshotAfterOpenFolder",
                SaveNotificationAction.CopyFile => "ScreenshotAfterCopyFile",
                SaveNotificationAction.CopyPath => "ScreenshotAfterCopyPath",
                _ => throw new ArgumentOutOfRangeException(nameof(action)),
            });
            ToolTip.SetTip(_open, _open.Content);
            _screen = screen ?? Screens.Primary;
            _message.Text = UiText.Format("AnimationExported", string.Join(Environment.NewLine, paths));
            _timer.Stop();
            Place();
            if (!IsVisible) Show();
            if (!IsPointerOver) _timer.Start();
        }

        private void Place()
        {
            if (_screen is not { } screen) return;
            var area = screen.WorkingArea;
            var scale = screen.Scaling;
            Width = Math.Min(360, Math.Max(160, area.Width / scale - 24));
            Position = new(
                area.Right - (int)Math.Ceiling((Width + 12) * scale),
                area.Bottom - (int)Math.Ceiling(((Bounds.Height > 0 ? Bounds.Height : MinHeight) + 12) * scale));
        }

        private void OnTimeout(object? sender, EventArgs args)
        {
            Close();
        }
    }
}

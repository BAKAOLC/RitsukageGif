using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;

namespace RitsukageGif.Desktop
{
    public sealed partial class MainWindow
    {
        private readonly Image _backgroundView = new()
        {
            Stretch = Stretch.UniformToFill,
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
        };

        private readonly TextBlock _captureRate = new() { Text = "—", HorizontalAlignment = HorizontalAlignment.Right };
        private readonly TextBlock _frames = new() { Text = "N/A", HorizontalAlignment = HorizontalAlignment.Right };

        private void BuildOriginalLayout()
        {
            ApplyBackground();
            using var iconStream = AssetLoader.Open(new("avares://RitsukageGif/!__AvaloniaDefaultWindowIcon"));
            Icon = new(iconStream);
            var root = new Grid();
            root.Children.Add(_backgroundView);
            var content = new Grid { RowDefinitions = new("*,Auto,22"), Margin = new(5) };
            var area = new Grid { ColumnDefinitions = new("110,*") };
            var left = new StackPanel
                { Spacing = 5, Margin = new(5, 5, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            StyleButton(_record, 100);
            StyleButton(_select, 80);
            var about = new Button { Content = UiText.Get("About") };
            StyleButton(about, 60);
            about.Click += async (_, _) => await new AboutWindow(Settings, _host) { Icon = Icon }.ShowDialog(this);
            left.Children.Add(_record);
            left.Children.Add(_select);
            left.Children.Add(about);
            left.Children.Add(new TextBlock { Text = UiText.Get("ScaleDivisor"), Margin = new(0, 3, 0, 0) });
            left.Children.Add(_scale);
            left.Children.Add(new TextBlock { Text = UiText.Get("FrameRate") });
            left.Children.Add(_fps);
            left.Children.Add(new TextBlock { Text = UiText.Get("OutputFormat") });
            left.Children.Add(_format);
            left.Children.Add(_cursor);
            area.Children.Add(left);
            var stats = new Grid
            {
                RowDefinitions = new("25,25,25,Auto,Auto"),
                ColumnDefinitions = new("Auto,*"),
                Margin = new(5, 5, 0, 0),
            };
            Grid.SetColumn(stats, 1);
            stats.Children.Add(new TextBlock { Text = UiText.Get("EncodingProgress") });
            Grid.SetColumn(_frames, 1);
            stats.Children.Add(_frames);
            var rateLabel = new TextBlock { Text = UiText.Get("CaptureRate") };
            Grid.SetRow(rateLabel, 1);
            stats.Children.Add(rateLabel);
            Grid.SetRow(_captureRate, 1);
            Grid.SetColumn(_captureRate, 1);
            stats.Children.Add(_captureRate);
            var sizeLabel = new TextBlock { Text = UiText.Get("FileSize") };
            Grid.SetRow(sizeLabel, 2);
            stats.Children.Add(sizeLabel);
            Grid.SetColumn(_size, 1);
            Grid.SetRow(_size, 2);
            _size.Text = "N/A";
            _size.HorizontalAlignment = HorizontalAlignment.Right;
            stats.Children.Add(_size);
            StyleButton(_configure, 60);
            _configure.Height = 28;
            Grid.SetRow(_configure, 3);
            Grid.SetColumnSpan(_configure, 2);
            _configure.HorizontalAlignment = HorizontalAlignment.Right;
            stats.Children.Add(_configure);
            StyleButton(_cancel, 90);
            _cancel.Height = 28;
            _cancel.HorizontalAlignment = HorizontalAlignment.Right;
            _cancel.Margin = new(0, 5, 0, 0);
            Grid.SetRow(_cancel, 4);
            Grid.SetColumnSpan(_cancel, 2);
            stats.Children.Add(_cancel);
            stats.VerticalAlignment = VerticalAlignment.Top;
            area.Children.Add(stats);
            _image.Width = 180;
            _image.Height = double.NaN;
            _image.MaxHeight = 130;
            _image.HorizontalAlignment = HorizontalAlignment.Right;
            _image.VerticalAlignment = VerticalAlignment.Bottom;
            _image.Margin = new(0, 280, 0, 0);
            // Separate rows reserve space for the controls even at large font scales.
            var upper = new Grid { RowDefinitions = new("Auto,Auto,*") };
            upper.Children.Add(area);
            ConfigureOutputSelector();
            Grid.SetRow(_outputSelector, 1);
            upper.Children.Add(_outputSelector);
            Grid.SetRow(_image, 2);
            _image.Margin = new(0, 5, 0, 0);
            upper.Children.Add(_image);
            content.Children.Add(upper);
            Grid.SetRow(_status, 1);
            _status.FontSize = 11;
            _status.MaxHeight = 48;
            _status.TextTrimming = TextTrimming.CharacterEllipsis;
            content.Children.Add(_status);
            var footer = new Grid { ColumnDefinitions = new("Auto,*,Auto") };
            footer.Children.Add(new TextBlock
            {
                Text = typeof(MainWindow).Assembly.GetName().Version!.ToString(),
                VerticalAlignment = VerticalAlignment.Bottom,
            });
            var open = new Button
            {
                Content = UiText.Get("OpenFolder"), FontSize = 11, Padding = new(2, 0), MinHeight = 0,
                Background = Brushes.Transparent, BorderThickness = new(0),
                VerticalAlignment = VerticalAlignment.Bottom,
            };
            open.Click += (_, _) =>
            {
                if (LastPath == null) return;
                try
                {
                    PlatformShell.RevealFile(LastPath);
                }
                catch (Exception ex)
                {
                    _host.SetStatus(ex.Message);
                }
            };
            Grid.SetColumn(_updateAvailable, 1);
            footer.Children.Add(_updateAvailable);
            Grid.SetColumn(open, 2);
            footer.Children.Add(open);
            Grid.SetRow(footer, 2);
            content.Children.Add(footer);
            root.Children.Add(content);
            Content = root;
            ToolTip.SetTip(_scale, UiText.Get("ScaleHint"));
            ToolTip.SetTip(_fps, UiText.Get("FrameRateHint"));
            ToolTip.SetTip(_select, _regionText.Text);
            Closed += (_, _) => DisposeBackground();
        }

        private static void StyleButton(Button button, double width)
        {
            button.Width = width;
            button.Height = 35;
            button.MinHeight = 0;
            button.MinWidth = 0;
            button.Padding = new(0);
            button.BorderThickness = new(1);
            button.HorizontalContentAlignment = HorizontalAlignment.Center;
            button.VerticalContentAlignment = VerticalAlignment.Center;
        }
    }
}

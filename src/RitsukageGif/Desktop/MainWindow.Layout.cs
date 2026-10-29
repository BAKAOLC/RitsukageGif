using Avalonia.Automation;
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
        private readonly TextBlock _frames = new() { Text = "—", HorizontalAlignment = HorizontalAlignment.Right };
        private readonly Button _presetsButton = new() { [!ContentProperty] = LiveText.Resource("PresetsTitle") };

        private void BuildOriginalLayout()
        {
            ApplyBackground();
            using var iconStream = AssetLoader.Open(new("avares://RitsukageGif/!__AvaloniaDefaultWindowIcon"));
            Icon = new(iconStream);
            var root = new Grid();
            root.Children.Add(_backgroundView);
            var content = new Grid { RowDefinitions = new("*,Auto,Auto,22"), Margin = new(5) };
            var area = new Grid { ColumnDefinitions = new("110,*") };
            var left = new StackPanel
                { Spacing = 5, Margin = new(5, 5, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            StyleButton(_record, 100);
            StyleButton(_select, 80);
            StyleButton(_presetsButton, 60);
            _presetsButton.Click += (_, _) => _presetsButton.ContextMenu?.Open(_presetsButton);
            left.Children.Add(_record);
            var selectionActions = new Grid { ColumnDefinitions = new("80,20"), Width = 100 };
            selectionActions.Children.Add(_select);
            StyleButton(_selectMenu, 20);
            _selectMenu.Padding = new(0);
            _selectMenu.Content = new PathIcon
            {
                Data = Geometry.Parse("M0,0 L8,0 4,4 Z"), Width = 8, Height = 4,
            };
            _selectMenu.Bind(ToolTip.TipProperty, LiveText.Resource("MainSourceMenu"));
            _selectMenu.Bind(AutomationProperties.NameProperty, LiveText.Resource("MainSourceMenu"));
            Grid.SetColumn(_selectMenu, 1);
            selectionActions.Children.Add(_selectMenu);
            left.Children.Add(selectionActions);
            left.Children.Add(_presetsButton);
            left.Children.Add(new TextBlock
                { [!TextBlock.TextProperty] = LiveText.Resource("ScaleDivisor"), Margin = new(0, 3, 0, 0) });
            left.Children.Add(_scale);
            left.Children.Add(new TextBlock { [!TextBlock.TextProperty] = LiveText.Resource("FrameRate") });
            left.Children.Add(_fps);
            left.Children.Add(new TextBlock { [!TextBlock.TextProperty] = LiveText.Resource("OutputFormat") });
            left.Children.Add(_format);
            foreach (var control in left.Children) control.HorizontalAlignment = HorizontalAlignment.Left;
            area.Children.Add(left);
            var stats = new Grid
            {
                RowDefinitions = new("25,25,25,Auto,Auto"),
                ColumnDefinitions = new("Auto,*"),
                Margin = new(5, 5, 0, 0),
            };
            Grid.SetColumn(stats, 1);
            stats.Children.Add(new TextBlock { [!TextBlock.TextProperty] = LiveText.Resource("EncodingProgress") });
            Grid.SetColumn(_frames, 1);
            stats.Children.Add(_frames);
            var rateLabel = new TextBlock { [!TextBlock.TextProperty] = LiveText.Resource("CaptureRate") };
            Grid.SetRow(rateLabel, 1);
            stats.Children.Add(rateLabel);
            Grid.SetRow(_captureRate, 1);
            Grid.SetColumn(_captureRate, 1);
            stats.Children.Add(_captureRate);
            var sizeLabel = new TextBlock { [!TextBlock.TextProperty] = LiveText.Resource("FileSize") };
            Grid.SetRow(sizeLabel, 2);
            stats.Children.Add(sizeLabel);
            Grid.SetColumn(_size, 1);
            Grid.SetRow(_size, 2);
            _size.Text = "—";
            _size.HorizontalAlignment = HorizontalAlignment.Right;
            stats.Children.Add(_size);
            StyleButton(_configure, 60);
            _configure.Height = 28;
            _configure.HorizontalAlignment = HorizontalAlignment.Right;
            var editAnimation = new Button
            {
                [!ContentProperty] = LiveText.Resource("AnimationEditorOpen"),
                Name = "OpenAnimationEditor",
            };
            StyleButton(editAnimation, double.NaN);
            editAnimation.MinWidth = 68;
            editAnimation.Height = 28;
            editAnimation.Bind(ToolTip.TipProperty, LiveText.Resource("AnimationEditorTitle"));
            editAnimation.Click += async (_, _) => await _host.ShowAnimationEditorAsync(LastPath);
            var actions = new Grid
            {
                Name = "MainActions", ColumnDefinitions = new("Auto,Auto"),
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            editAnimation.Margin = new(0, 0, 5, 0);
            actions.Children.Add(editAnimation);
            Grid.SetColumn(_configure, 1);
            actions.Children.Add(_configure);
            Grid.SetRow(actions, 3);
            Grid.SetColumnSpan(actions, 2);
            stats.Children.Add(actions);
            editAnimation.SizeChanged += (_, _) => FitActionButtons();
            _configure.SizeChanged += (_, _) => FitActionButtons();

            StyleButton(_cancel, 90);
            _cancel.Height = 28;
            _cancel.HorizontalAlignment = HorizontalAlignment.Right;
            _cancel.Margin = new(0, 5, 0, 0);
            Grid.SetRow(_cancel, 4);
            Grid.SetColumnSpan(_cancel, 2);
            stats.Children.Add(_cancel);
            stats.VerticalAlignment = VerticalAlignment.Top;
            area.Children.Add(stats);
            _previewSurface.Width = 180;
            _previewSurface.Height = double.NaN;
            _previewSurface.MaxHeight = 130;
            _previewSurface.HorizontalAlignment = HorizontalAlignment.Right;
            _previewSurface.VerticalAlignment = VerticalAlignment.Bottom;
            _previewSurface.Margin = new(0, 280, 0, 0);
            // Separate rows reserve space for the controls even at large font scales.
            var upper = new Grid { RowDefinitions = new("Auto,Auto,*") };
            upper.Children.Add(area);
            ConfigureOutputSelector();
            Grid.SetRow(_outputSelector, 1);
            upper.Children.Add(_outputSelector);
            Grid.SetRow(_previewSurface, 2);
            _previewSurface.Margin = new(0, 5, 0, 0);
            upper.Children.Add(_previewSurface);
            content.Children.Add(upper);
            _regionText.FontSize = 11;
            _regionText.TextWrapping = TextWrapping.NoWrap;
            _regionText.TextTrimming = TextTrimming.CharacterEllipsis;
            _regionText.Margin = new(0, 3, 0, 2);
            Grid.SetRow(_regionText, 1);
            content.Children.Add(_regionText);
            Grid.SetRow(_status, 2);
            _status.FontSize = 11;
            _status.MaxHeight = 48;
            _status.TextTrimming = TextTrimming.CharacterEllipsis;
            content.Children.Add(_status);
            var footer = new Grid { ColumnDefinitions = new("Auto,*,Auto") };
            footer.Children.Add(new TextBlock
            {
                Text = ApplicationVersion.Display,
                MaxWidth = 160,
                TextTrimming = TextTrimming.CharacterEllipsis,
                [ToolTip.TipProperty] = ApplicationVersion.Display,
                VerticalAlignment = VerticalAlignment.Bottom,
            });
            var open = new Button
            {
                [!ContentProperty] = LiveText.Resource("OpenFolder"), FontSize = 11, Padding = new(2, 0),
                MinHeight = 0,
                Background = Brushes.Transparent, BorderThickness = new(0),
                VerticalAlignment = VerticalAlignment.Bottom,
            };
            open.Classes.Add("link");
            _updateAvailable.Classes.Add("link");
            open.Click += async (_, _) =>
            {
                var path = _host.LastScreenshotOutput is not null ? _host.LatestOutputPath : LastPath;
                var configuredDirectory = Settings.OutputDirectory;
                open.IsEnabled = false;
                try
                {
                    await Task.Run(() =>
                    {
                        var directory = string.IsNullOrWhiteSpace(path)
                            ? configuredDirectory
                            : Path.GetDirectoryName(Path.GetFullPath(path))!;
                        PlatformShell.OpenDirectory(directory);
                    });
                }
                catch (Exception ex)
                {
                    _host.SetStatus(ex.Message);
                }
                finally
                {
                    open.IsEnabled = true;
                }
            };
            Grid.SetColumn(_updateAvailable, 1);
            footer.Children.Add(_updateAvailable);
            Grid.SetColumn(open, 2);
            footer.Children.Add(open);
            Grid.SetRow(footer, 3);
            content.Children.Add(footer);
            root.Children.Add(content);
            Content = root;
            _scale.Bind(ToolTip.TipProperty, LiveText.Resource("ScaleHint"));
            _fps.Bind(ToolTip.TipProperty, LiveText.Resource("FrameRateHint"));
            ToolTip.SetTip(_select, _regionText.Text);
            Closed += (_, _) => DisposeBackground();
            return;

            void FitActionButtons()
            {
                var required = editAnimation.Bounds.Width + editAnimation.Margin.Left + editAnimation.Margin.Right +
                               _configure.Bounds.Width + _configure.Margin.Left + _configure.Margin.Right +
                               content.Margin.Left + content.Margin.Right +
                               area.ColumnDefinitions[0].Width.Value + stats.Margin.Left + stats.Margin.Right;
                MinWidth = Math.Max(271, required);
                if (Width < MinWidth) Width = MinWidth;
            }
        }

        private static void StyleButton(Button button, double width)
        {
            button.Width = width;
            button.Height = 35;
            button.FontSize = 12;
            button.MinHeight = 0;
            button.MinWidth = 0;
            button.Padding = new(3, 0);
            button.BorderThickness = new(1);
            button.HorizontalContentAlignment = HorizontalAlignment.Center;
            button.VerticalContentAlignment = VerticalAlignment.Center;
        }
    }
}

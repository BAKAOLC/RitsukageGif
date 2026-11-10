using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class ImageTranslationWindow
    {
        private static TextBox CreateText(bool readOnly)
        {
            return new()
            {
                AcceptsReturn = true, IsReadOnly = readOnly, TextWrapping = TextWrapping.Wrap,
                VerticalContentAlignment = VerticalAlignment.Top,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Stretch,
            };
        }

        private static StackPanel LanguagePanel(string key, Control picker)
        {
            var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            label.Bind(TextBlock.TextProperty, LiveText.Resource(key));
            return new()
            {
                Orientation = Orientation.Horizontal, Spacing = 6, Margin = new(0, 0, 8, 0),
                Children = { label, picker },
            };
        }

        private static Button IconButton(string geometryKey, string labelKey)
        {
            var button = new Button { Width = 30, Height = 30, Padding = new(5) };
            button.Content = ToolbarIcon.Create(button,
                ToolbarIcon.FindDefinition(geometryKey) ?? ToolbarIcon.GetDefinition(geometryKey));
            button.Bind(ToolTip.TipProperty, LiveText.Resource(labelKey));
            return button;
        }

        private Grid TextPanel(string key, TextBox input, Button speech)
        {
            var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            label.Bind(TextBlock.TextProperty, LiveText.Resource(key));
            var count = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            var copy = IconButton("ScreenshotCopy", "ScreenshotCopy");
            copy.IsEnabled = !string.IsNullOrEmpty(input.Text);
            input.TextChanged += (_, _) =>
            {
                count.Text = (input.Text?.Length ?? 0).ToString(CultureInfo.CurrentCulture);
                copy.IsEnabled = !string.IsNullOrEmpty(input.Text);
            };
            copy.Click += async (_, _) => await CopyTextAsync(input.Text);
            var header = new Grid { ColumnDefinitions = new("*,Auto,Auto,Auto"), ColumnSpacing = 6 };
            header.Children.Add(label);
            Grid.SetColumn(count, 1);
            header.Children.Add(count);
            Grid.SetColumn(speech, 2);
            header.Children.Add(speech);
            Grid.SetColumn(copy, 3);
            header.Children.Add(copy);
            var panel = new Grid { RowDefinitions = new("Auto,*"), RowSpacing = 6 };
            panel.Children.Add(header);
            Grid.SetRow(input, 1);
            panel.Children.Add(input);
            return panel;
        }

        private void BuildImagePanel()
        {
            var translated = new ToggleButton
            {
                IsChecked = true, Height = 30, HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center, CornerRadius = new(4, 0, 0, 4),
            };
            var original = new ToggleButton
            {
                Height = 30, HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center, CornerRadius = new(0, 4, 4, 0),
            };
            translated.Bind(ContentProperty, LiveText.Resource("TranslationTranslatedImage"));
            original.Bind(ContentProperty, LiveText.Resource("TranslationOriginalImage"));
            translated.IsCheckedChanged += (_, _) =>
            {
                if (translated.IsChecked == true)
                {
                    original.IsChecked = false;
                    _originalImage = false;
                    RefreshImage();
                }
                else if (original.IsChecked != true)
                {
                    translated.IsChecked = true;
                }
            };
            original.IsCheckedChanged += (_, _) =>
            {
                if (original.IsChecked == true)
                {
                    translated.IsChecked = false;
                    _originalImage = true;
                    RefreshImage();
                }
                else if (translated.IsChecked != true)
                {
                    original.IsChecked = true;
                }
            };
            var choices = new Grid { ColumnDefinitions = new("*,*") };
            choices.Children.Add(translated);
            Grid.SetColumn(original, 1);
            choices.Children.Add(original);
            _copyImage.Width = 30;
            _copyImage.Height = 30;
            _copyImage.Padding = new(5);
            _copyImage.Content = ToolbarIcon.Create(_copyImage,
                ToolbarIcon.FindDefinition("ScreenshotCopy") ?? ToolbarIcon.GetDefinition("ScreenshotCopy"));
            _copyImage.Bind(ToolTip.TipProperty, LiveText.Resource("ScreenshotAfterCopyImage"));
            _copyImage.Click += async (_, _) => await CopyImageAsync();
            var zoomIn = IconButton("AnimationZoomIn", "AnimationZoomIn");
            var zoomOut = IconButton("AnimationZoomOut", "AnimationZoomOut");
            var fit = IconButton("AnimationFit", "TranslationFitToWindow");
            var actual = IconButton("AnimationActualSize", "AnimationActualSize");
            zoomIn.Click += (_, _) => _viewport.SetZoom(_viewport.Zoom * 1.25);
            zoomOut.Click += (_, _) => _viewport.SetZoom(_viewport.Zoom / 1.25);
            fit.Click += (_, _) => _viewport.FitToViewport();
            actual.Click += (_, _) => _viewport.ActualSize();
            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 4,
                Children = { _copyImage, zoomIn, zoomOut, fit, actual },
            };
            var header = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8 };
            header.Children.Add(choices);
            Grid.SetColumn(actions, 1);
            header.Children.Add(actions);
            _image.Children.Add(header);
            Grid.SetRow(_viewport, 1);
            _image.Children.Add(_viewport);
        }

        private void RefreshImage()
        {
            var bitmap = _originalImage ? _originalBitmap : _translatedBitmap ?? _originalBitmap;
            _imageSurface.Source = bitmap;
            _imageSurface.Document = _originalImage ? _sourceImageText : _translatedImageText;
            _viewport.PixelSize = bitmap.PixelSize;
            _copyImage.IsEnabled = _originalImage || _translatedPng is not null;
        }

        private void ApplyDisplay()
        {
            var imageMode = _preferences.Mode == TranslationMode.ImageToImage;
            var textVisible = !imageMode || _preferences.ImageDisplayMode != TranslationImageDisplayMode.ImageOnly;
            var imageVisible = imageMode && _preferences.ImageDisplayMode != TranslationImageDisplayMode.TextOnly;
            _texts.IsVisible = textVisible;
            _image.IsVisible = imageVisible;
            _splitter.IsVisible = textVisible && imageVisible;
            _panes.ColumnDefinitions[0].Width = textVisible ? new(1, GridUnitType.Star) : new(0);
            _panes.ColumnDefinitions[1].Width = textVisible && imageVisible ? new(5) : new(0);
            _panes.ColumnDefinitions[2].Width = imageVisible ? new(1, GridUnitType.Star) : new(0);
        }

        private void PersistView()
        {
            if (_updating) return;
            _preferences.WindowTopmost = Topmost;
            _preferencesChanged?.Invoke(_preferences with { });
        }

        private void SetBusy(bool busy)
        {
            _run.IsEnabled = !busy;
            _stop.IsEnabled = busy;
        }

        private void SetStatus(string key, params object[] arguments)
        {
            _statusKey = key;
            _statusArguments = arguments;
            _status.Text = arguments.Length == 0 ? UiText.Get(key) : UiText.Format(key, arguments);
        }

        private void RefreshLanguage()
        {
            if (_lifetime.IsCancellationRequested) return;
            if (_statusKey is not null)
                _status.Text = _statusArguments.Length == 0
                    ? UiText.Get(_statusKey)
                    : UiText.Format(_statusKey, _statusArguments);
            _updating = true;
            var index = _display.SelectedIndex;
            _display.ItemsSource = new[]
            {
                UiText.Get("TranslationTextAndImage"), UiText.Get("TranslationImageOnly"),
                UiText.Get("TranslationTextOnly"),
            };
            _display.SelectedIndex = index;
            _updating = false;
        }
    }
}

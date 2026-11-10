using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class RecognitionWindow
    {
        private readonly Action? _copyDirectlyNextTime;
        private readonly Action<RecognitionDialogPreferences, RecognitionDialogPreferences>? _dialogPreferencesChanged;
        private readonly Action<bool>? _originalImageVisibilityChanged;
        private readonly ISpeechPlaybackService? _speechService;
        private readonly CheckBox _copyDirectly = new();
        private readonly ImageViewport _originalView = new() { ImageBacking = ImageTransparencyBackground.Checker };
        private readonly ImageViewport _loadingView = new() { ImageBacking = ImageTransparencyBackground.Checker };
        private readonly Grid _textPanes = new();
        private readonly Grid _textContent = new();
        private readonly Grid _textWorking = new();
        private readonly GridSplitter _textSplitter = new();
        private readonly Button _textLanguage = new();
        private readonly TextBlock _textError = new() { TextWrapping = TextWrapping.Wrap };
        private RecognitionDialogPreferences _dialogOptions;
        private TranslationSpeechControls? _speechControls;
        private Bitmap? _originalBitmap;
        private Task _originalTask = Task.CompletedTask;
        private ImageTextSelectionSurface? _originalSurface;
        private OcrRecognitionResult? _recognizedResult;
        private string _recognizedPlainText;
        private bool _copyCompleted;
        private bool _closingCopy;
        private bool _originalAbove;

        private Grid BuildTextContent()
        {
            var root = new Grid { Margin = new(10) };
            _textContent.RowDefinitions = new("Auto,Auto,*,Auto");
            _textContent.RowSpacing = 8;
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            _copy.Name = "RecognitionCopy";
            _copy.Classes.Add("primary");
            _copy.MinWidth = 68;
            _copy.Content = TextActionContent(_copy, "ScreenshotCopy", "ScreenshotCopy");
            actions.Children.Add(_copy);
            Opened += (_, _) =>
            {
                _speechControls = new(this, SelectedOrAllText, () => "", () => _recognizedResult?.LanguageTag ??
                                                                               _preferences.LanguageTag, () => null,
                    text => _status.Text = text, _lifetime.Token, _speechService);
                actions.Children.Insert(1, _speechControls.SourceButton);
            };
            actions.Children.Add(TextMenuButton("RecognitionLayoutSettings", "ToolbarAdjust", CreateTextLayoutMenu));
            actions.Children.Add(TextMenuButton("Settings", "GeneralHeading", CreateTextCopyMenu));
            var showOriginal = new CheckBox
            {
                Name = "RecognitionShowOriginal", IsChecked = _preferences.ShowOriginalImage,
                IsVisible = !_image.IsEmpty, VerticalAlignment = VerticalAlignment.Center, Margin = new(8, 0, 0, 0),
                [!ContentProperty] = LiveText.Resource("RecognitionOriginalImage"),
            };
            showOriginal.IsCheckedChanged += (_, _) =>
            {
                SetOriginalVisible(showOriginal.IsChecked == true);
                _originalImageVisibilityChanged?.Invoke(showOriginal.IsChecked == true);
            };
            actions.Children.Add(showOriginal);
            _textContent.Children.Add(actions);
            Grid.SetRow(_textError, 1);
            _textContent.Children.Add(_textError);
            _status.PropertyChanged += (_, change) =>
            {
                if (change.Property == TextBlock.TextProperty) UpdateTextState();
            };
            _text.VerticalContentAlignment = VerticalAlignment.Top;
            _textPanes.Children.Add(_text);
            _textPanes.Children.Add(_textSplitter);
            _textPanes.Children.Add(_originalView);
            Grid.SetRow(_textPanes, 2);
            _textContent.Children.Add(_textPanes);
            _copyDirectly.Name = "RecognitionCopyDirectlyNextTime";
            _copyDirectly.IsVisible = _copyDirectlyNextTime is not null;
            _copyDirectly.Bind(ContentProperty, LiveText.Resource("RecognitionCopyDirectlyNextTime"));
            var bottom = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8 };
            bottom.Children.Add(_copyDirectly);
            _textLanguage.Name = "RecognitionLanguage";
            _textLanguage.MinWidth = 96;
            _textLanguage.IsVisible = !_image.IsEmpty;
            _textLanguage.Click += (_, _) => ShowTextLanguages();
            Grid.SetColumn(_textLanguage, 1);
            bottom.Children.Add(_textLanguage);
            Grid.SetRow(bottom, 3);
            _textContent.Children.Add(bottom);
            _textWorking.RowDefinitions = new("Auto,*");
            var workingActions = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8,
                Children = { _status, _install, _run, _cancel },
            };
            _textWorking.Children.Add(workingActions);
            Grid.SetRow(_loadingView, 1);
            _textWorking.Children.Add(_loadingView);
            root.Children.Add(_textWorking);
            root.Children.Add(_textContent);
            _text.PropertyChanged += (_, change) =>
            {
                if (change.Property == TextBox.SelectionStartProperty ||
                    change.Property == TextBox.SelectionEndProperty)
                    TextSelectionChanged();
            };
            Closing += ClosingTextRecognition;
            AddHandler(KeyDownEvent, TextRecognitionShortcut, RoutingStrategies.Tunnel);
            Topmost = _dialogOptions.Topmost;
            if (!_image.IsEmpty)
            {
                var dimensions = PngImageCodec.ReadDimensions(_image.Span);
                _originalAbove = (double)dimensions.Height / dimensions.Width < 0.8;
            }

            Width = _originalAbove ? 900 : 824;
            Height = _originalAbove ? 700 : 612;
            ConfigureTextPanes();
            SetOriginalVisible(showOriginal.IsChecked == true);
            Opened += (_, _) => _originalTask = LoadTextOriginalAsync();
            return WithTextTitleBar(root);
        }

        private static StackPanel TextActionContent(Button button, string label, string icon)
        {
            return new()
            {
                Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    ToolbarIcon.Create(button, ToolbarIcon.FindDefinition(icon) ??
                                               throw new InvalidOperationException(
                                                   "Recognition toolbar icon is not registered.")),
                    new TextBlock
                    {
                        [!TextBlock.TextProperty] = LiveText.Resource(label),
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                },
            };
        }

        private void ConfigureTextPanes()
        {
            if (_originalAbove)
            {
                _textPanes.RowDefinitions = new("*,5,*");
                _textPanes.ColumnDefinitions = new("*");
                Grid.SetRow(_originalView, 0);
                Grid.SetRow(_textSplitter, 1);
                Grid.SetRow(_text, 2);
                _textSplitter.ResizeDirection = GridResizeDirection.Rows;
                _textSplitter.HorizontalAlignment = HorizontalAlignment.Stretch;
            }
            else
            {
                _textPanes.ColumnDefinitions = new("*,5,*");
                _textPanes.RowDefinitions = new("*");
                Grid.SetColumn(_text, 0);
                Grid.SetColumn(_textSplitter, 1);
                Grid.SetColumn(_originalView, 2);
                _textSplitter.ResizeDirection = GridResizeDirection.Columns;
                _textSplitter.VerticalAlignment = VerticalAlignment.Stretch;
            }
        }

        private void SetOriginalVisible(bool visible)
        {
            visible &= !_image.IsEmpty;
            _originalView.IsVisible = _textSplitter.IsVisible = visible;
            if (_originalAbove)
            {
                _textPanes.RowDefinitions[0].Height = visible ? new(1, GridUnitType.Star) : new(0);
                _textPanes.RowDefinitions[1].Height = new(visible ? 5 : 0);
            }
            else
            {
                _textPanes.ColumnDefinitions[2].Width = visible ? new(1, GridUnitType.Star) : new(0);
                _textPanes.ColumnDefinitions[1].Width = new(visible ? 5 : 0);
            }
        }

        private async Task LoadTextOriginalAsync()
        {
            if (_image.IsEmpty) return;
            try
            {
                var token = _lifetime.Token;
                var dimensions = PngImageCodec.ReadDimensions(_image.Span);
                var width = Math.Max(1, Math.Min(dimensions.Width,
                    (int)Math.Round(dimensions.Width *
                                    Math.Min(1, 2048d / Math.Max(dimensions.Width, dimensions.Height)))));
                var bitmap = await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    using var stream = MemoryMarshal.TryGetArray(_image, out var data)
                        ? new MemoryStream(data.Array!, data.Offset, data.Count, false)
                        : new MemoryStream(_image.ToArray(), false);
                    return Bitmap.DecodeToWidth(stream, width);
                }, token);
                if (token.IsCancellationRequested)
                {
                    bitmap.Dispose();
                    return;
                }

                _originalBitmap = bitmap;
                _originalSurface = new() { Source = bitmap, SelectionBrush = AppTheme.Accent };
                _originalSurface.CopyRequested += text =>
                {
                    if (_copyTask.IsCompleted) _copyTask = CopyOriginalTextAsync(text);
                };
                EnsureOriginalTextDocument();
                _originalSurface.Document = _originalTextDocument;
                _originalView.ImageContent = _originalSurface;
                _originalView.PixelSize = new(dimensions.Width, dimensions.Height);
                _loadingView.Source = bitmap;
                TextSelectionChanged();
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                if (!_lifetime.IsCancellationRequested) _status.Text = error.Message;
            }
        }

        private void UpdateTextState()
        {
            var hasText = !string.IsNullOrEmpty(_text.Text);
            _textContent.IsVisible = !_busy && hasText;
            _textWorking.IsVisible = !_textContent.IsVisible;
            _run.IsVisible = !_busy && !_image.IsEmpty;
            _cancel.IsVisible = _busy;
            _textLanguage.IsEnabled = !_busy && _languagesLoaded;
            _textLanguage.Content = (_language.SelectedItem as ComboBoxItem)?.Content ??
                                    UiText.Get("RecognitionAutomatic");
            _textError.Text = _status.Text;
            _textError.IsVisible = !_busy && !string.IsNullOrWhiteSpace(_status.Text);
            _speechControls?.Refresh();
        }

        private void ShowTextLanguages()
        {
            var menu = new ContextMenu();
            foreach (var item in _language.Items.OfType<ComboBoxItem>())
            {
                var choice = new MenuItem
                {
                    Header = item.Content, ToggleType = MenuItemToggleType.Radio,
                    GroupName = "recognition-language", IsChecked = ReferenceEquals(item, _language.SelectedItem),
                };
                choice.Click += (_, _) =>
                {
                    _language.SelectedItem = item;
                    _operation = RecognizeAsync();
                };
                menu.Items.Add(choice);
            }

            menu.Open(_textLanguage);
        }
    }
}

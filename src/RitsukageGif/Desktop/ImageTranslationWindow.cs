using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ImageTranslationWindow : Window
    {
        private readonly Button _copyImage = new() { IsEnabled = false };
        private readonly ComboBox _display = new();
        private readonly Grid _image = new() { RowDefinitions = new("Auto,*"), RowSpacing = 6 };
        private readonly ImageTextSelectionSurface _imageSurface = new() { SelectionBrush = AppTheme.Accent };
        private readonly CancellationTokenSource _lifetime = new();
        private readonly ITextRecognitionService _ocr;
        private readonly Bitmap _originalBitmap;
        private readonly Grid _panes = new() { ColumnDefinitions = new("*,5,*") };
        private readonly ReadOnlyMemory<byte> _png;
        private readonly TranslationPreferences _preferences;
        private readonly Action<TranslationPreferences>? _preferencesChanged;
        private readonly RecognitionPreferences _recognition;
        private readonly Button _run = new();
        private readonly TranslationLanguagePicker _sourceLanguage = new(true);
        private readonly TextBox _sourceText = CreateText(false);
        private readonly TranslationSpeechControls _speech;

        private readonly GridSplitter _splitter = new()
            { ResizeDirection = GridResizeDirection.Columns, HorizontalAlignment = HorizontalAlignment.Stretch };

        private readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center };
        private readonly Button _stop = new();
        private readonly Button _swap;
        private readonly TranslationLanguagePicker _targetLanguage = new();
        private readonly Grid _texts = new() { RowDefinitions = new("*,*") };
        private readonly TextBox _translatedText = CreateText(true);
        private readonly ImageViewport _viewport = new() { ImageBacking = ImageTransparencyBackground.Checker };
        private ImageTranslationDocument? _document;
        private bool _loopRunning;
        private Task _operation = Task.CompletedTask;
        private bool _originalImage;
        private bool _pending;
        private OcrRecognitionResult? _recognized;
        private string? _recognizedLanguage;
        private ImageTextSelectionDocument _sourceImageText = ImageTextSelectionDocument.Empty;
        private object[] _statusArguments = [];
        private string? _statusKey;
        private Bitmap? _translatedBitmap;
        private ImageTextSelectionDocument _translatedImageText = ImageTextSelectionDocument.Empty;
        private byte[]? _translatedPng;
        private bool _updating;
        private CancellationTokenSource? _work;

        private ImageTranslationWindow(ReadOnlyMemory<byte> png, TranslationPreferences preferences,
            RecognitionPreferences recognition, ITextRecognitionService ocr,
            Action<TranslationPreferences>? preferencesChanged, ISpeechPlaybackService? speechService = null)
        {
            _png = png;
            _preferences = preferences.Normalize();
            _recognition = recognition;
            _ocr = ocr;
            _preferencesChanged = preferencesChanged;
            _originalBitmap = new(new MemoryStream(png.ToArray(), false));
            _imageSurface.Source = _originalBitmap;
            _imageSurface.CopyRequested += text => _ = CopyTextAsync(text);
            _viewport.ImageContent = _imageSurface;
            _viewport.PixelSize = _originalBitmap.PixelSize;
            _sourceLanguage.LanguageTag = _preferences.SourceLanguage;
            _targetLanguage.LanguageTag = _preferences.TargetLanguage;
            Width = _preferences.Mode == TranslationMode.ImageToImage ? 1100 : 780;
            Height = 720;
            MinWidth = 720;
            MinHeight = 480;
            Topmost = _preferences.WindowTopmost;
            Background = AppTheme.Surface;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Bind(TitleProperty, LiveText.Resource("TranslationTitle"));
            _speech = new(this, () => _sourceText.Text ?? "", () => _translatedText.Text ?? "",
                () => _document?.SourceLanguage ?? _sourceLanguage.LanguageTag,
                () => _document?.TargetLanguage ?? _targetLanguage.LanguageTag,
                text => _status.Text = text, _lifetime.Token, speechService);
            _swap = IconButton("SettingsTransferHeading", "TranslationSwapLanguages");
            _swap.Click += (_, _) =>
            {
                if (!CanSwapLanguages) return;
                _updating = true;
                var source = EffectiveSourceLanguage;
                _sourceLanguage.LanguageTag = _targetLanguage.LanguageTag;
                _targetLanguage.LanguageTag = source;
                _updating = false;
                StartTranslation();
            };
            var topmost = new CheckBox { IsChecked = Topmost, VerticalAlignment = VerticalAlignment.Center };
            topmost.Bind(ContentProperty, LiveText.Resource("PinnedImageTopmost"));
            topmost.IsCheckedChanged += (_, _) =>
            {
                Topmost = topmost.IsChecked == true;
                PersistView();
            };
            var languages = new WrapPanel { Orientation = Orientation.Horizontal };
            languages.Children.Add(LanguagePanel("TranslationSourceLanguage", _sourceLanguage));
            languages.Children.Add(_swap);
            languages.Children.Add(LanguagePanel("TranslationTargetLanguage", _targetLanguage));
            languages.Children.Add(topmost);
            var showOriginal = new CheckBox
                { IsChecked = _preferences.ShowOriginalText, VerticalAlignment = VerticalAlignment.Center };
            showOriginal.Bind(ContentProperty, LiveText.Resource("TranslationShowOriginalText"));
            var sourcePanel = TextPanel("TranslationOriginal", _sourceText, _speech.SourceButton);
            var targetPanel = TextPanel("TranslationResult", _translatedText, _speech.TranslatedButton);
            sourcePanel.IsVisible = _preferences.ShowOriginalText;
            showOriginal.IsCheckedChanged += (_, _) =>
            {
                _preferences.ShowOriginalText = showOriginal.IsChecked == true;
                sourcePanel.IsVisible = _preferences.ShowOriginalText;
                _texts.RowDefinitions[0].Height = _preferences.ShowOriginalText ? new(1, GridUnitType.Star) : new(0);
                PersistView();
            };
            _texts.RowDefinitions[0].Height = _preferences.ShowOriginalText ? new(1, GridUnitType.Star) : new(0);
            _texts.Children.Add(sourcePanel);
            Grid.SetRow(targetPanel, 1);
            _texts.Children.Add(targetPanel);
            BuildImagePanel();
            _panes.Children.Add(_texts);
            Grid.SetColumn(_splitter, 1);
            _panes.Children.Add(_splitter);
            Grid.SetColumn(_image, 2);
            _panes.Children.Add(_image);
            _display.ItemsSource = new[]
            {
                UiText.Get("TranslationTextAndImage"), UiText.Get("TranslationImageOnly"),
                UiText.Get("TranslationTextOnly"),
            };
            _display.SelectedIndex = (int)_preferences.ImageDisplayMode;
            _display.IsVisible = _preferences.Mode == TranslationMode.ImageToImage;
            _display.SelectionChanged += (_, _) =>
            {
                if (_display.SelectedIndex < 0) return;
                _preferences.ImageDisplayMode = (TranslationImageDisplayMode)_display.SelectedIndex;
                ApplyDisplay();
                PersistView();
            };
            _run.Bind(ContentProperty, LiveText.Resource("TranslationRun"));
            _stop.Bind(ContentProperty, LiveText.Resource("TranslationStop"));
            _run.Click += (_, _) => StartTranslation();
            _stop.Click += (_, _) =>
            {
                _pending = false;
                _work?.Cancel();
            };
            var tools = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 6, Children = { showOriginal, _display, _run, _stop },
            };
            var layout = new Grid { RowDefinitions = new("Auto,Auto,*,Auto"), RowSpacing = 8, Margin = new(12) };
            layout.Children.Add(languages);
            Grid.SetRow(tools, 1);
            layout.Children.Add(tools);
            Grid.SetRow(_panes, 2);
            layout.Children.Add(_panes);
            Grid.SetRow(_status, 3);
            layout.Children.Add(_status);
            Content = layout;
            _sourceLanguage.LanguageTagChanged += (_, _) =>
            {
                if (!_updating) StartTranslation();
                _swap.IsEnabled = CanSwapLanguages;
                _speech.Refresh();
            };
            _targetLanguage.LanguageTagChanged += (_, _) =>
            {
                if (!_updating) StartTranslation();
                _swap.IsEnabled = CanSwapLanguages;
                _speech.Refresh();
            };
            _sourceText.PropertyChanged += (_, change) =>
            {
                if (change.Property != TextBox.TextProperty) return;
                if (!_updating)
                {
                    _pending = false;
                    _work?.Cancel();
                    SetStatus("TranslationRetry");
                }

                _speech.Refresh();
            };
            _translatedText.TextChanged += (_, _) => _speech.Refresh();
            UiText.LanguageChanged += RefreshLanguage;
            Closed += (_, _) =>
            {
                UiText.LanguageChanged -= RefreshLanguage;
                _pending = false;
                _lifetime.Cancel();
            };
            ApplyDisplay();
            SetBusy(false);
            _swap.IsEnabled = CanSwapLanguages;
        }

        private string EffectiveSourceLanguage =>
            _sourceLanguage.LanguageTag.Equals("auto", StringComparison.OrdinalIgnoreCase)
                ? _document?.SourceLanguage ?? "auto"
                : _sourceLanguage.LanguageTag;

        private bool CanSwapLanguages => EffectiveSourceLanguage.Length > 0 &&
                                         !EffectiveSourceLanguage.Equals("auto", StringComparison.OrdinalIgnoreCase);

        internal static async Task ShowAsync(Window owner, ReadOnlyMemory<byte> png, TranslationPreferences preferences,
            RecognitionPreferences recognition, ITextRecognitionService ocr, bool autoTranslate = true,
            Action<TranslationPreferences>? viewPreferencesChanged = null, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            var window = new ImageTranslationWindow(png, preferences, recognition, ocr, viewPreferencesChanged);
            await using var registration = token.Register(() => Dispatcher.UIThread.Post(window.Close));
            if (autoTranslate) window.Opened += (_, _) => window.StartTranslation();
            try
            {
                var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                window.Closed += (_, _) => completion.TrySetResult();
                window.Show();
                if (owner.IsVisible)
                {
                    var ownerBounds = new Avalonia.PixelRect(owner.Position,
                        new PixelSize((int)Math.Round(owner.Bounds.Width * owner.RenderScaling),
                            (int)Math.Round(owner.Bounds.Height * owner.RenderScaling)));
                    var width = (int)Math.Round(window.Width * window.RenderScaling);
                    var height = (int)Math.Round(window.Height * window.RenderScaling);
                    var x = ownerBounds.Center.X - width / 2;
                    var y = ownerBounds.Center.Y - height / 2;
                    if (owner.Screens.ScreenFromWindow(owner) is { WorkingArea: var area })
                    {
                        x = Math.Clamp(x, area.X, Math.Max(area.X, area.Right - width));
                        y = Math.Clamp(y, area.Y, Math.Max(area.Y, area.Bottom - height));
                    }

                    window.Position = new(x, y);
                }

                await completion.Task;
            }
            finally
            {
                await window._lifetime.CancelAsync();
                await window._operation;
                await window._speech.DisposeAsync();
                window._imageSurface.Document = ImageTextSelectionDocument.Empty;
                window._imageSurface.Source = null;
                window._viewport.ImageContent = null;
                window._originalBitmap.Dispose();
                window._translatedBitmap?.Dispose();
                window._lifetime.Dispose();
            }
        }
    }
}

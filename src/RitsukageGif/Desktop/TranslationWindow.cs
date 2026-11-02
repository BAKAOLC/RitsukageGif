using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class TranslationWindow : Window
    {
        private readonly Button _copy = new();
        private readonly Button _fallback = new() { IsVisible = false };
        private readonly TranslationLanguagePicker _fallbackLanguage = new(allowNone: true);
        private readonly CancellationTokenSource _lifetime = new();
        private readonly TranslationPreferences _preferences;
        private readonly Button _run = new();
        private readonly TranslationLanguagePicker _sourceLanguage = new(true);

        private readonly TextBox _sourceText = new()
        {
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top,
            TextAlignment = TextAlignment.Left,
        };

        private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
        private readonly Button _stop = new();
        private readonly TranslationLanguagePicker _targetLanguage = new();

        private readonly TextBox _translatedText = new()
        {
            AcceptsReturn = true, IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top,
            TextAlignment = TextAlignment.Left,
        };

        private Task _operation = Task.CompletedTask;
        private long _revision;
        private object[] _statusArguments = [];
        private string? _statusResourceKey;
        private CancellationTokenSource? _work;

        private TranslationWindow(string text, TranslationPreferences preferences, bool autoTranslate,
            ISpeechPlaybackService? speechService = null)
        {
            _preferences = preferences.Normalize();
            _sourceText.Text = text;
            _sourceLanguage.LanguageTag = _preferences.SourceLanguage;
            _targetLanguage.LanguageTag = _preferences.TargetLanguage;
            _fallbackLanguage.LanguageTag = _preferences.FallbackLanguage;
            Width = 960;
            Height = 600;
            MinWidth = 720;
            MinHeight = 420;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = AppTheme.Surface;
            Bind(TitleProperty, LiveText.Resource("TranslationTitle"));
            _run.Bind(ContentProperty, LiveText.Resource("TranslationRun"));
            _stop.Bind(ContentProperty, LiveText.Resource("TranslationStop"));
            _copy.Bind(ContentProperty, LiveText.Resource("ScreenshotCopy"));
            _fallback.Bind(ContentProperty, LiveText.Resource("TranslationUseFallback"));
            var close = new Button();
            close.Bind(ContentProperty, LiveText.Resource("Close"));
            close.Click += (_, _) => Close();
            _run.Click += (_, _) => StartTranslation();
            _stop.Click += (_, _) => _work?.Cancel();
            _fallback.Click += (_, _) =>
            {
                _targetLanguage.LanguageTag = _fallbackLanguage.LanguageTag;
                StartTranslation();
            };
            _copy.Click += async (_, _) => await CopyAsync();
            foreach (var input in new Control[] { _sourceText, _sourceLanguage, _targetLanguage, _fallbackLanguage })
                input.PropertyChanged += (_, change) =>
                {
                    if (input is TranslationLanguagePicker
                            ? change.Property != TranslationLanguagePicker.LanguageTagProperty
                            : change.Property != TextBox.TextProperty) return;
                    _revision++;
                    _work?.Cancel();
                    _fallback.IsVisible = false;
                    _translatedText.Text = "";
                    SetStatus(null);
                };
            _translatedText.TextChanged += (_, _) => _copy.IsEnabled = !string.IsNullOrEmpty(_translatedText.Text);
            var languages = new WrapPanel { Orientation = Orientation.Horizontal };
            AddLanguage("TranslationSourceLanguage", _sourceLanguage);
            AddLanguage("TranslationTargetLanguage", _targetLanguage);
            AddLanguage("TranslationFallbackLanguage", _fallbackLanguage);
            var texts = new Grid { ColumnDefinitions = new("*,*"), ColumnSpacing = 12 };
            var original = TextPanel("TranslationOriginal", _sourceText);
            var translated = TextPanel("TranslationResult", _translatedText);
            InitializeSpeech(original, translated, speechService);
            texts.Children.Add(original);
            Grid.SetColumn(translated, 1);
            texts.Children.Add(translated);
            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8, Children = { _fallback, _run, _stop, _copy, close },
            };
            var footer = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 12 };
            footer.Children.Add(_status);
            Grid.SetColumn(actions, 1);
            footer.Children.Add(actions);
            var layout = new Grid { RowDefinitions = new("Auto,*,Auto"), RowSpacing = 12, Margin = new(16) };
            layout.Children.Add(languages);
            Grid.SetRow(texts, 1);
            layout.Children.Add(texts);
            Grid.SetRow(footer, 2);
            layout.Children.Add(footer);
            Content = layout;
            SetBusy(false);
            UiText.LanguageChanged += LanguageChanged;
            Closed += (_, _) =>
            {
                UiText.LanguageChanged -= LanguageChanged;
                _lifetime.Cancel();
            };
            if (autoTranslate) Opened += (_, _) => StartTranslation();
            return;

            void AddLanguage(string key, Control input)
            {
                var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
                label.Bind(TextBlock.TextProperty, LiveText.Resource(key));
                languages.Children.Add(new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 6, Margin = new(0, 0, 16, 0),
                    Children = { label, input },
                });
            }
        }

        internal static async Task ShowAsync(Window owner, string text, TranslationPreferences preferences,
            bool autoTranslate = false, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            var window = new TranslationWindow(text, preferences, autoTranslate);
            await using var registration = token.Register(() => Dispatcher.UIThread.Post(window.Close));
            try
            {
                if (owner.IsVisible)
                {
                    await window.ShowDialog(owner);
                }
                else
                {
                    var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    window.Closed += (_, _) => closed.TrySetResult();
                    window.Show();
                    await closed.Task;
                }
            }
            finally
            {
                await window._lifetime.CancelAsync();
                await window._operation;
                if (window._speech is not null) await window._speech.DisposeAsync();
                window._lifetime.Dispose();
            }
        }

        private static Grid TextPanel(string key, TextBox input)
        {
            var label = new TextBlock();
            label.Bind(TextBlock.TextProperty, LiveText.Resource(key));
            var panel = new Grid { RowDefinitions = new("Auto,*"), RowSpacing = 6 };
            panel.Children.Add(label);
            Grid.SetRow(input, 1);
            panel.Children.Add(input);
            return panel;
        }

        private void SetBusy(bool busy)
        {
            _run.IsEnabled = !busy;
            _stop.IsEnabled = busy;
            _fallback.IsEnabled = !busy;
            _copy.IsEnabled = !busy && !string.IsNullOrEmpty(_translatedText.Text);
        }

        private void SetStatus(string? resourceKey, params object[] arguments)
        {
            _statusResourceKey = resourceKey;
            _statusArguments = arguments;
            RefreshStatus();
        }

        private void LanguageChanged()
        {
            if (Dispatcher.UIThread.CheckAccess()) RefreshStatus();
            else Dispatcher.UIThread.Post(RefreshStatus);
        }

        private void RefreshStatus()
        {
            if (_lifetime.IsCancellationRequested) return;
            _status.Text = _statusResourceKey is null ? "" :
                _statusArguments.Length == 0 ? UiText.Get(_statusResourceKey) :
                UiText.Format(_statusResourceKey, _statusArguments);
        }
    }
}

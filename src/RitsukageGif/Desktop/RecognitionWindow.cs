using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed class RecognitionWindow : Window
    {
        private readonly BarcodeRecognitionService _barcode = new();
        private readonly Button _cancel = new();

        private readonly ComboBox _codes = new()
            { IsVisible = false, HorizontalAlignment = HorizontalAlignment.Stretch };

        private readonly Button _copy = new();
        private readonly ReadOnlyMemory<byte> _image;
        private readonly ComboBox _language = new() { MinWidth = 220 };
        private readonly CancellationTokenSource _lifetime = new();
        private readonly RecognitionMode _mode;
        private readonly OcrRecognitionService _ocr = new();
        private readonly RecognitionPreferences _preferences;
        private readonly Button _run = new();
        private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };

        private readonly TextBox _text = new()
        {
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
        };

        private bool _busy;
        private bool _languagesLoaded;
        private Task _operation = Task.CompletedTask;
        private bool _recognitionReported;
        private Func<CancellationToken, Task>? _recognized;
        private CancellationTokenSource? _work;

        private RecognitionWindow(ReadOnlyMemory<byte> image, RecognitionMode mode, RecognitionPreferences preferences,
            string? initialText)
        {
            _image = image;
            _mode = mode;
            _preferences = preferences.Normalize();
            Width = 680;
            Height = 500;
            MinWidth = 420;
            MinHeight = 300;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = AppTheme.Surface;
            Bind(TitleProperty, LiveText.Resource(mode == RecognitionMode.Text ? "RecognizeText" : "RecognizeBarcode"));
            _run.Bind(ContentProperty, LiveText.Resource("RecognitionRetry"));
            _cancel.Bind(ContentProperty, LiveText.Resource("Cancel"));
            _copy.Bind(ContentProperty, LiveText.Resource("ScreenshotCopy"));
            var close = new Button();
            close.Bind(ContentProperty, LiveText.Resource("Close"));
            close.Click += (_, _) => Close();
            _run.Click += (_, _) => StartRecognition();
            _cancel.Click += (_, _) => _work?.Cancel();
            _copy.Click += async (_, _) =>
            {
                try
                {
                    var clipboard = Clipboard ?? throw new InvalidOperationException(UiText.Get("ClipboardFailed"));
                    await clipboard.SetTextAsync(_text.Text ?? "");
                }
                catch (Exception error)
                {
                    _status.Text = error.Message;
                }
            };
            _text.TextChanged += (_, _) => _copy.IsEnabled = !string.IsNullOrEmpty(_text.Text);
            _codes.SelectionChanged += (_, _) =>
            {
                if (_codes.SelectedItem is ComboBoxItem { Tag: BarcodeRecognitionResult result })
                    _text.Text = result.Text;
            };
            var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            label.Bind(TextBlock.TextProperty, LiveText.Resource("RecognitionLanguage"));
            var languages = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8, IsVisible = mode == RecognitionMode.Text,
                Children = { label, _language },
            };
            var header = new StackPanel { Spacing = 8, Children = { languages, _codes, _status } };
            var footer = new StackPanel
            {
                Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8, Children = { _run, _cancel, _copy, close },
            };
            var layout = new Grid { RowDefinitions = new("Auto,*,Auto"), RowSpacing = 12, Margin = new(16) };
            layout.Children.Add(header);
            Grid.SetRow(_text, 1);
            layout.Children.Add(_text);
            Grid.SetRow(footer, 2);
            layout.Children.Add(footer);
            Content = layout;
            if (initialText is null) Opened += (_, _) => StartRecognition();
            else _text.Text = initialText;
            Closed += (_, _) => _lifetime.Cancel();
            SetBusy(false);
        }

        internal static async Task ShowAsync(Window owner, ReadOnlyMemory<byte> image, RecognitionMode mode,
            Func<CancellationToken, Task>? recognized = null,
            RecognitionPreferences? preferences = null, string? initialText = null, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            var window = new RecognitionWindow(image, mode, preferences ?? new(), initialText)
                { _recognized = recognized };
            await using var registration = token.Register(() => Dispatcher.UIThread.Post(window.Close));
            try
            {
                await window.ShowDialog(owner);
            }
            finally
            {
                await window._lifetime.CancelAsync();
                await window._operation;
                window._lifetime.Dispose();
            }
        }

        private void StartRecognition()
        {
            if (_busy || _lifetime.IsCancellationRequested) return;
            _operation = RecognizeAsync();
        }

        private async Task RecognizeAsync()
        {
            using var work = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _work = work;
            SetBusy(true);
            _text.Text = "";
            _codes.Items.Clear();
            _codes.IsVisible = false;
            _status.Text = UiText.Get("RecognitionWorking");
            try
            {
                if (_mode == RecognitionMode.Text)
                {
                    if (!_languagesLoaded)
                    {
                        var capabilities = await _ocr.GetCapabilitiesAsync(work.Token);
                        work.Token.ThrowIfCancellationRequested();
                        if (!capabilities.Available)
                        {
                            _status.Text = UiText.Get("RecognitionUnavailable");
                            ToolTip.SetTip(_status, capabilities.FailureReason);
                            return;
                        }

                        _language.Items.Clear();
                        var automatic = new ComboBoxItem();
                        automatic.Bind(ContentProperty, LiveText.Resource("RecognitionAutomatic"));
                        _language.Items.Add(automatic);
                        foreach (var language in capabilities.Languages)
                            _language.Items.Add(new ComboBoxItem
                                { Content = language.DisplayName, Tag = language.Tag });
                        var preferred = RecognitionOutput.Language(_preferences, capabilities.Languages);
                        _language.SelectedItem = _language.Items.OfType<ComboBoxItem>()
                            .FirstOrDefault(item => Equals(item.Tag, preferred)) ?? automatic;
                        _languagesLoaded = true;
                    }

                    var languageTag = (_language.SelectedItem as ComboBoxItem)?.Tag as string;
                    var result = await _ocr.RecognizeImageAsync(_image, languageTag, work.Token);
                    work.Token.ThrowIfCancellationRequested();
                    _text.Text = RecognitionTextFormatter.Format(result, _preferences);
                }
                else
                {
                    var results = await _barcode.RecognizeAsync(_image, work.Token);
                    work.Token.ThrowIfCancellationRequested();
                    _codes.Items.Clear();
                    for (var index = 0; index < results.Count; index++)
                    {
                        var result = results[index];
                        _codes.Items.Add(new ComboBoxItem { Content = $"{index + 1}: {result.Format}", Tag = result });
                    }

                    _codes.IsVisible = results.Count > 0;
                    _codes.SelectedIndex = results.Count > 0 ? 0 : -1;
                    _text.Text = results.Count > 0 ? results[0].Text : "";
                }

                _status.Text = string.IsNullOrWhiteSpace(_text.Text) ? UiText.Get("RecognitionEmpty") : "";
                if (!_recognitionReported && !string.IsNullOrWhiteSpace(_text.Text) && _recognized is { } recognized)
                {
                    work.Token.ThrowIfCancellationRequested();
                    await recognized(work.Token);
                    _recognitionReported = true;
                }
            }
            catch (OperationCanceledException) when (work.IsCancellationRequested)
            {
                if (!_lifetime.IsCancellationRequested) _status.Text = UiText.Get("RecognitionCancelled");
            }
            catch (Exception error)
            {
                if (!_lifetime.IsCancellationRequested) _status.Text = error.Message;
            }
            finally
            {
                _work = null;
                SetBusy(false);
            }
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            _run.IsEnabled = !busy;
            _cancel.IsEnabled = busy;
            _language.IsEnabled = !busy && _languagesLoaded;
            _text.IsEnabled = !busy;
            _copy.IsEnabled = !string.IsNullOrEmpty(_text.Text);
        }
    }
}

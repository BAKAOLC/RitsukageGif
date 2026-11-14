using System.Runtime.ExceptionServices;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class RecognitionWindow : Window
    {
        private readonly BarcodeRecognitionService _barcode = new();
        private readonly Button _cancel = new();

        private readonly ComboBox _codes = new()
            { IsVisible = false, HorizontalAlignment = HorizontalAlignment.Stretch };

        private readonly Button _copy = new();
        private readonly ReadOnlyMemory<byte> _image;
        private readonly Button _install = new();

        private readonly ComboBox _language = new()
            { MinWidth = 120, HorizontalAlignment = HorizontalAlignment.Stretch };

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

        private readonly Button _translate = new();

        private bool _busy;
        private bool _languagesLoaded;
        private Task _operation = Task.CompletedTask;
        private Exception? _recognitionError;
        private bool _recognitionReported;
        private Func<CancellationToken, Task>? _recognized;
        private CancellationTokenSource? _work;

        private RecognitionWindow(ReadOnlyMemory<byte> image, RecognitionMode mode, RecognitionPreferences preferences,
            string? initialText, OcrRecognitionResult? initialResult, TranslationPreferences? translationPreferences,
            Action? copyDirectlyNextTime,
            Action<RecognitionDialogPreferences, RecognitionDialogPreferences>? dialogPreferencesChanged,
            Action<bool>? originalImageVisibilityChanged, ISpeechPlaybackService? speech)
        {
            _image = image;
            _mode = mode;
            _preferences = preferences.Normalize();
            _dialogOptions = _preferences.Dialog;
            _copyDirectlyNextTime = copyDirectlyNextTime;
            _dialogPreferencesChanged = dialogPreferencesChanged;
            _originalImageVisibilityChanged = originalImageVisibilityChanged;
            _speechService = speech;
            _recognizedResult = initialResult;
            _recognizedPlainText = initialResult?.Text ?? initialText ?? "";
            _recognitionReported = !string.IsNullOrWhiteSpace(_recognizedPlainText);
            Width = 680;
            Height = 500;
            MinWidth = 420;
            MinHeight = 300;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = AppTheme.Surface;
            Bind(TitleProperty, LiveText.Resource(mode == RecognitionMode.Text ? "RecognizeText" : "RecognizeBarcode"));
            _run.Bind(ContentProperty, LiveText.Resource("RecognitionRetry"));
            _cancel.Bind(ContentProperty, LiveText.Resource("Cancel"));
            _install.Bind(ContentProperty, LiveText.Resource("RecognitionInstallComponents"));
            _install.IsVisible = mode == RecognitionMode.Text && !image.IsEmpty;
            _install.Click += (_, _) =>
            {
                if (!_busy) _operation = InstallAsync();
            };
            if (mode != RecognitionMode.Text) _copy.Bind(ContentProperty, LiveText.Resource("ScreenshotCopy"));
            _translate.Bind(ContentProperty, LiveText.Resource("TranslationRun"));
            _translate.IsVisible = translationPreferences is not null;
            _translate.Click += async (_, _) =>
            {
                if (_busy || string.IsNullOrWhiteSpace(_text.Text) || translationPreferences is null) return;
                try
                {
                    await TranslationWindow.ShowAsync(this, _text.Text, translationPreferences,
                        true, _lifetime.Token);
                }
                catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
                {
                }
                catch (Exception error)
                {
                    if (!_lifetime.IsCancellationRequested) _status.Text = error.Message;
                }
            };
            var close = new Button();
            close.Bind(ContentProperty, LiveText.Resource("Close"));
            close.Click += (_, _) => Close();
            _run.Click += (_, _) => StartRecognition();
            _cancel.Click += (_, _) => _work?.Cancel();
            _copy.Click += (_, _) =>
            {
                if (_copyTask.IsCompleted) _copyTask = CopyButtonAsync();
            };
            _text.TextChanged += (_, _) =>
            {
                _copy.IsEnabled = !string.IsNullOrEmpty(_text.Text);
                _translate.IsEnabled = !_busy && !string.IsNullOrWhiteSpace(_text.Text);
                RefreshTextWordRanges();
                TextSelectionChanged();
            };
            _codes.SelectionChanged += (_, _) =>
            {
                if (_codes.SelectedItem is ComboBoxItem { Tag: BarcodeRecognitionResult result })
                    _text.Text = result.Text;
            };
            if (mode == RecognitionMode.Text)
            {
                Content = BuildTextContent();
            }
            else
            {
                var header = new StackPanel { Spacing = 8, Children = { _codes, _status } };
                var footer = new StackPanel
                {
                    Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8, Children = { _run, _cancel, _translate, _copy, close },
                };
                var layout = new Grid { RowDefinitions = new("Auto,*,Auto"), RowSpacing = 12, Margin = new(16) };
                layout.Children.Add(header);
                Grid.SetRow(_text, 1);
                layout.Children.Add(_text);
                Grid.SetRow(footer, 2);
                layout.Children.Add(footer);
                Content = layout;
            }

            if (initialText is null && initialResult is null)
            {
                Opened += (_, _) => StartRecognition();
            }
            else
            {
                if (mode == RecognitionMode.Text) ApplyTextFormatting();
                else _text.Text = initialText;
                if (mode == RecognitionMode.Text && !image.IsEmpty)
                    Opened += (_, _) => _operation = InitializeLanguagesAsync();
            }

            Closed += (_, _) =>
            {
                if (_mode == RecognitionMode.Text && _copyDirectly.IsChecked == true) _copyDirectlyNextTime?.Invoke();
                _lifetime.Cancel();
            };
            SetBusy(false);
        }

        internal static async Task ShowAsync(Window? owner, ReadOnlyMemory<byte> image, RecognitionMode mode,
            Func<CancellationToken, Task>? recognized = null,
            RecognitionPreferences? preferences = null, string? initialText = null,
            TranslationPreferences? translationPreferences = null,
            Action<FormulaCopyFormat>? copyFormatChanged = null, Action<bool>? originalImageVisibilityChanged = null,
            Action<FormulaLatexFormat>? latexFormatChanged = null,
            Action? copyDirectlyNextTime = null,
            Action<RecognitionDialogPreferences, RecognitionDialogPreferences>? dialogPreferencesChanged = null,
            ISpeechPlaybackService? speech = null, OcrRecognitionResult? initialResult = null,
            Func<int>? imageSaveQuality = null, bool requireResult = false, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (mode is RecognitionMode.Table or RecognitionMode.Formula)
            {
                var normalized = (preferences ?? new()).Normalize();
                await StructuredRecognitionWindow.ShowAsync(owner, image, mode == RecognitionMode.Formula,
                    recognized, token, normalized.CopyFormat, copyFormatChanged,
                    normalized.ShowOriginalImage, originalImageVisibilityChanged, normalized.LatexFormat,
                    latexFormatChanged, imageSaveQuality, normalized.Dialog, dialogPreferencesChanged,
                    requireResult);
                return;
            }

            var window = new RecognitionWindow(image, mode, preferences ?? new(), initialText, initialResult,
                    translationPreferences,
                    copyDirectlyNextTime, dialogPreferencesChanged, originalImageVisibilityChanged, speech)
                { _recognized = recognized };
            if (mode == RecognitionMode.Text) window.FitTextDialogToScreen(owner);
            await using var registration = token.Register(static state =>
                Dispatcher.UIThread.Post(((RecognitionWindow)state!).CancelAndClose), window);
            try
            {
                if (owner is { IsVisible: true })
                {
                    await window.ShowDialog(owner);
                }
                else
                {
                    var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    window.Closed += (_, _) => completion.TrySetResult();
                    window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                    window.Show();
                    await completion.Task;
                }
            }
            finally
            {
                await window._lifetime.CancelAsync();
                await window._operation;
                await window._copyTask;
                await window._closingCopyTask;
                await window._originalTask;
                if (window._speechControls is { } controls) await controls.DisposeAsync();
                window._originalBitmap?.Dispose();
                window._lifetime.Dispose();
            }

            if (requireResult) EnsureRecognitionCompleted(window._recognitionReported, window._recognitionError, token);
        }

        internal static void EnsureRecognitionCompleted(bool completed, Exception? error, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (completed) return;
            if (error is not null) ExceptionDispatchInfo.Throw(error);
            throw new OperationCanceledException("Screenshot recognition cancelled.", new CancellationToken(true));
        }

        private void CancelAndClose()
        {
            if (!IsVisible) return;
            _lifetime.Cancel();
            Close();
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
            _recognitionError = null;
            try
            {
                if (_mode == RecognitionMode.Text)
                {
                    if (!await LoadLanguagesAsync(work.Token)) return;

                    var languageTag = (_language.SelectedItem as ComboBoxItem)?.Tag as string;
                    var result = await _ocr.RecognizeImageAsync(_image, languageTag, work.Token);
                    work.Token.ThrowIfCancellationRequested();
                    _recognizedResult = result;
                    _recognizedPlainText = result.Text;
                    ApplyTextFormatting();
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

                if (string.IsNullOrWhiteSpace(_text.Text))
                {
                    _status.Text = UiText.Get("RecognitionEmpty");
                    _recognitionError = new InvalidOperationException(_status.Text);
                }
                else
                {
                    _status.Text = "";
                    if (!_recognitionReported)
                    {
                        if (_recognized is { } recognized) await recognized(work.Token);
                        _recognitionReported = true;
                    }
                }
            }
            catch (OperationCanceledException) when (work.IsCancellationRequested)
            {
                if (!_lifetime.IsCancellationRequested) _status.Text = UiText.Get("RecognitionCancelled");
            }
            catch (Exception error)
            {
                _recognitionError = error;
                if (!_lifetime.IsCancellationRequested) _status.Text = error.Message;
            }
            finally
            {
                _work = null;
                SetBusy(false);
            }
        }

        private async Task InitializeLanguagesAsync()
        {
            using var work = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _work = work;
            UpdateTextState();
            try
            {
                await LoadLanguagesAsync(work.Token);
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
                UpdateTextState();
            }
        }

        private async Task<bool> LoadLanguagesAsync(CancellationToken token)
        {
            if (_languagesLoaded) return true;
            var offline = await StructuredRecognitionProcess.Shared.GetTextCapabilitiesAsync(token);
            token.ThrowIfCancellationRequested();
            _install.IsVisible = !offline.Available;
            var capabilities = await _ocr.GetCapabilitiesAsync(token);
            token.ThrowIfCancellationRequested();
            if (!capabilities.Available)
            {
                _status.Text = UiText.Get("RecognitionUnavailable");
                _recognitionError = new InvalidOperationException(capabilities.FailureReason ?? _status.Text);
                ToolTip.SetTip(_status, capabilities.FailureReason);
                return false;
            }

            ToolTip.SetTip(_status, null);
            _language.Items.Clear();
            var automatic = new ComboBoxItem();
            automatic.Bind(ContentProperty, LiveText.Resource("RecognitionAutomatic"));
            _language.Items.Add(automatic);
            foreach (var language in capabilities.Languages)
                _language.Items.Add(new ComboBoxItem { Content = language.DisplayName, Tag = language.Tag });
            var preferred = RecognitionOutput.Language(_preferences, capabilities.Languages);
            _language.SelectedItem = _language.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => Equals(item.Tag, preferred)) ?? automatic;
            _languagesLoaded = true;
            return true;
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            _run.IsEnabled = !busy && !_image.IsEmpty;
            _install.IsEnabled = !busy;
            _cancel.IsEnabled = busy;
            _language.IsEnabled = !busy && _languagesLoaded;
            _text.IsEnabled = !busy;
            _copy.IsEnabled = !string.IsNullOrEmpty(_text.Text);
            _translate.IsEnabled = !busy && !string.IsNullOrWhiteSpace(_text.Text);
            if (_mode == RecognitionMode.Text) UpdateTextState();
        }

        private async Task InstallAsync()
        {
            using var work = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _work = work;
            SetBusy(true);
            var installed = false;
            try
            {
                await RecognitionComponentInstaller.InstallAsync(StructuredRecognitionKind.Text,
                    new Progress<string>(text =>
                    {
                        if (!_lifetime.IsCancellationRequested) _status.Text = text;
                    }), work.Token);
                _languagesLoaded = false;
                installed = true;
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

            if (installed && !_lifetime.IsCancellationRequested) await RecognizeAsync();
        }
    }
}

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed class TranslationSpeechControls : IAsyncDisposable
    {
        private static readonly ILogger Logger = LogHub.GetLogger("Desktop.TranslationSpeech");
        private readonly Task _capabilityQuery;
        private readonly Action<string> _error;
        private readonly CancellationTokenSource _lifetime;
        private readonly Window _owner;
        private readonly ISpeechPlaybackService _service;
        private readonly Func<string?> _sourceLanguage, _translatedLanguage;
        private readonly Func<string> _sourceText, _translatedText;
        private int _activeSide;
        private SpeechCapabilities? _capabilities;
        private Task? _disposeTask;
        private bool _disposed;
        private Task _operation = Task.CompletedTask;
        private CancellationTokenSource? _speaking;
        private string? _spokenText, _spokenVoice;

        internal TranslationSpeechControls(Window owner, Func<string> sourceText, Func<string> translatedText,
            Func<string?> sourceLanguage, Func<string?> translatedLanguage, Action<string> error,
            CancellationToken lifetime,
            ISpeechPlaybackService? service = null)
        {
            _owner = owner;
            _sourceText = sourceText;
            _translatedText = translatedText;
            _sourceLanguage = sourceLanguage;
            _translatedLanguage = translatedLanguage;
            _error = error;
            _service = service ?? new SystemSpeechPlaybackService();
            _lifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
            SourceButton.Click += async (_, _) => await ToggleAsync(1);
            TranslatedButton.Click += async (_, _) => await ToggleAsync(2);
            owner.Closed += Closed;
            UiText.LanguageChanged += Refresh;
            Refresh();
            _capabilityQuery = LoadCapabilitiesAsync();
        }

        internal Button SourceButton { get; } = CreateButton();
        internal Button TranslatedButton { get; } = CreateButton();

        public ValueTask DisposeAsync()
        {
            return new(_disposeTask ??= DisposeCoreAsync());
        }

        internal void Refresh()
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(Refresh);
                return;
            }

            if (_disposed) return;
            if (_activeSide != 0 && _capabilities is not null)
            {
                var text = TextFor(_activeSide);
                var voice = SpeechVoiceSelection.Select(_capabilities, LanguageFor(_activeSide));
                if (text != _spokenText || voice?.Id != _spokenVoice) _speaking?.Cancel();
            }

            Update(SourceButton, 1);
            Update(TranslatedButton, 2);
        }

        private async Task LoadCapabilitiesAsync()
        {
            try
            {
                _capabilities = await _service.GetCapabilitiesAsync(_lifetime.Token);
            }
            catch (OperationCanceledException)when (_lifetime.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                Logger.ZLogWarning(error, $"System speech capabilities could not be loaded.");
                _capabilities = new(false, [], Reason: SpeechUnavailableReason.EngineError);
            }
            finally
            {
                Refresh();
            }
        }

        private async Task ToggleAsync(int side)
        {
            if (_disposed) return;
            if (_activeSide == side && _speaking is not null)
            {
                await StopAsync();
                return;
            }

            var text = TextFor(side);
            var language = LanguageFor(side);
            var voice = _capabilities is null ? null : SpeechVoiceSelection.Select(_capabilities, language);
            if (string.IsNullOrWhiteSpace(text) || voice is null) return;
            if (_speaking is { } speaking) await speaking.CancelAsync();
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _speaking = cancellation;
            _activeSide = side;
            _spokenText = text;
            _spokenVoice = voice.Id;
            Refresh();
            _operation = SpeakAsync(text, language, cancellation);
            await _operation;
        }

        private async Task SpeakAsync(string text, string? language, CancellationTokenSource cancellation)
        {
            try
            {
                await _service.SpeakAsync(text, language, cancellation.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception error)
            {
                Logger.ZLogWarning(error, $"Translation speech failed.");
                if (!_lifetime.IsCancellationRequested) _error(UiText.Get("SpeechUnavailable"));
            }
            finally
            {
                if (ReferenceEquals(_speaking, cancellation))
                {
                    _speaking = null;
                    _activeSide = 0;
                    _spokenText = _spokenVoice = null;
                }

                cancellation.Dispose();
                Refresh();
            }
        }

        private async Task StopAsync()
        {
            if (_speaking is { } speaking) await speaking.CancelAsync();
            await _service.StopAsync();
            await _operation;
        }

        private string TextFor(int side)
        {
            return side == 1 ? _sourceText() : _translatedText();
        }

        private string? LanguageFor(int side)
        {
            return side == 1 ? _sourceLanguage() : _translatedLanguage();
        }

        private void Update(Button button, int side)
        {
            var playing = _activeSide == side;
            var key = playing ? "SpeechStop" : side == 1 ? "SpeechSpeakSource" : "SpeechSpeakTranslation";
            var voice = _capabilities is null ? null : SpeechVoiceSelection.Select(_capabilities, LanguageFor(side));
            button.IsEnabled = playing || (voice is not null && !string.IsNullOrWhiteSpace(TextFor(side)));
            button.Opacity = button.IsEnabled ? 1 : 0.4;
            var tip = UiText.Get(key);
            if (!playing)
            {
                if (_capabilities is null) tip = UiText.Get("SpeechChecking");
                else if (!_capabilities.Available)
                    tip = _capabilities.Reason switch
                    {
                        SpeechUnavailableReason.EngineMissing => UiText.Format("SpeechEngineMissing",
                            _capabilities.Detail ?? "speech"),
                        SpeechUnavailableReason.NoVoices => UiText.Get("SpeechNoVoices"),
                        _ => UiText.Get("SpeechUnavailable"),
                    };
                else if (voice is null) tip = UiText.Format("SpeechVoiceUnavailable", LanguageFor(side) ?? "auto");
            }

            ToolTip.SetTip(button, tip);
            AutomationProperties.SetName(button, UiText.Get(key));
            if (!Equals(button.Tag, key))
            {
                button.Tag = key;
                button.Content = ToolbarIcon.Create(button,
                    FluentToolbarIcon.Geometry(playing ? "SpeechStop" : "SpeechSpeak") ??
                    throw new InvalidOperationException("Speech toolbar icon is not registered."));
            }
        }

        private static Button CreateButton()
        {
            var button = new Button
            {
                Width = 24, Height = 24, MinWidth = 0, MinHeight = 0, Padding = new(4), Margin = new(0),
                BorderThickness = new(0), Background = Brushes.Transparent, Focusable = false,
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTip.SetShowOnDisabled(button, true);
            return button;
        }

        private void Closed(object? sender, EventArgs args)
        {
            _ = DisposeAsync().AsTask();
        }

        private async Task DisposeCoreAsync()
        {
            _disposed = true;
            _owner.Closed -= Closed;
            UiText.LanguageChanged -= Refresh;
            await _lifetime.CancelAsync();
            try
            {
                await _capabilityQuery;
                await StopAsync();
                await _service.DisposeAsync();
            }
            catch (Exception error)
            {
                Logger.ZLogWarning(error, $"Translation speech cleanup completed with an error.");
            }
            finally
            {
                _lifetime.Dispose();
            }
        }
    }
}

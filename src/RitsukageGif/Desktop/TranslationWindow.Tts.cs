using Avalonia.Controls;
using Avalonia.Layout;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class TranslationWindow
    {
        private string? _detectedSpeechLanguage;
        private TranslationSpeechControls? _speech;

        private void InitializeSpeech(Grid original, Grid translated, ISpeechPlaybackService? service)
        {
            _speech = new(this, () => _sourceText.Text ?? "", () => _translatedText.Text ?? "",
                () => _detectedSpeechLanguage ?? _sourceLanguage.LanguageTag, () => _targetLanguage.LanguageTag,
                message =>
                {
                    _statusResourceKey = null;
                    _status.Text = message;
                }, _lifetime.Token, service);
            Attach(original, _speech.SourceButton);
            Attach(translated, _speech.TranslatedButton);
            _sourceText.TextChanged += (_, _) =>
            {
                _detectedSpeechLanguage = null;
                _speech.Refresh();
            };
            _translatedText.TextChanged += (_, _) => _speech.Refresh();
            _sourceLanguage.PropertyChanged += (_, change) =>
            {
                if (change.Property != TranslationLanguagePicker.LanguageTagProperty) return;
                _detectedSpeechLanguage = null;
                _speech.Refresh();
            };
            _targetLanguage.PropertyChanged += (_, change) =>
            {
                if (change.Property == TranslationLanguagePicker.LanguageTagProperty) _speech.Refresh();
            };
            return;

            static void Attach(Grid panel, Button button)
            {
                var label = panel.Children[0];
                panel.Children.Remove(label);
                var header = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 4 };
                label.VerticalAlignment = VerticalAlignment.Center;
                header.Children.Add(label);
                Grid.SetColumn(button, 1);
                header.Children.Add(button);
                panel.Children.Add(header);
            }
        }
    }
}

using Avalonia.Controls;
using Avalonia.Input.Platform;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        internal Func<Window, ReadOnlyMemory<byte>, CancellationToken, Task>? ImageTranslationRequested { get; set; }
        internal Func<TranslationPreferences> TranslationPreferencesProvider { get; set; } = () => new();
        internal Action<FormulaCopyFormat>? FormulaCopyFormatChanged { get; set; }
        internal Action<bool>? RecognitionOriginalImageVisibilityChanged { get; set; }
        internal Action<FormulaLatexFormat>? FormulaLatexFormatChanged { get; set; }

        internal Action<RecognitionDialogPreferences, RecognitionDialogPreferences>? RecognitionDialogPreferencesChanged
        {
            get;
            set;
        }

        internal Action<RecognitionConfirmationKind>? DirectRecognitionCopyRequested { get; set; }

        private void AddRecognitionMenu(ContextMenu menu)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(Command("TranslateScreenshot", TranslateAsync));
        }

        private async Task TranslateAsync()
        {
            var output = await CaptureOutputAsync(OutputColorMode.Sdr, _lifetime.Token);
            if (ImageTranslationRequested is { } translate)
                await translate(this, output.Png, _lifetime.Token);
            else
                await ImageTranslationWindow.ShowAsync(this, output.Png, TranslationPreferencesProvider(),
                    RecognitionPreferencesProvider(),
                    TextRecognitionServiceProvider?.Invoke() ?? DefaultTextRecognition, token: _lifetime.Token);
            await SaveAutomaticallyAsync(ScreenshotAutoSaveTriggers.Translate, output);
        }

        private async Task<(byte[] Png, ScreenshotResult? SaveOutput)> PrepareRecognitionInputAsync()
        {
            var token = _lifetime.Token;
            token.ThrowIfCancellationRequested();
            if (AnimationActive || LiveCaptureActive)
            {
                var current = await CaptureOutputAsync(OutputColorMode.Sdr, token);
                return (current.Png, current);
            }

            var source = _sdrPng ?? _png;
            var saveOutput = AutomaticSave is not null &&
                             (SavePreferencesProvider().Normalize().AutoSaveTriggers &
                              ScreenshotAutoSaveTriggers.Recognize) != 0
                ? await CaptureOutputAsync(OutputColorMode.Sdr, token)
                : null;
            return (source, saveOutput);
        }

        private async Task RecognizeAsync(RecognitionMode mode)
        {
            var (png, output) = await PrepareRecognitionInputAsync();
            var preferences = RecognitionPreferencesProvider().Normalize();
            if (mode == RecognitionMode.Text && !preferences.ShowDialogOnRecognition)
            {
                var recognition = await RecognitionOutput.RecognizeAsync(png, preferences,
                    TextRecognitionServiceProvider?.Invoke() ?? DefaultTextRecognition, _lifetime.Token);
                var text = RecognitionTextFormatter.Format(recognition, preferences);
                if (string.IsNullOrEmpty(text)) throw new InvalidOperationException(UiText.Get("RecognitionEmpty"));
                var clipboard = Clipboard ?? throw new InvalidOperationException(UiText.Get("ClipboardFailed"));
                await clipboard.SetTextAsync(text);
                await clipboard.FlushAsync();
                if (output is not null) await SaveAutomaticallyAsync(ScreenshotAutoSaveTriggers.Recognize, output);
                return;
            }

            await RecognitionWindow.ShowAsync(this, png, mode,
                output is null ? null : _ => SaveAutomaticallyAsync(ScreenshotAutoSaveTriggers.Recognize, output),
                preferences,
                token: _lifetime.Token, translationPreferences: TranslationPreferencesProvider(),
                copyFormatChanged: FormulaCopyFormatChanged,
                originalImageVisibilityChanged: RecognitionOriginalImageVisibilityChanged,
                latexFormatChanged: FormulaLatexFormatChanged,
                imageSaveQuality: () => SavePreferencesProvider().Quality,
                copyDirectlyNextTime: () =>
                    DirectRecognitionCopyRequested?.Invoke(RecognitionConfirmationKind.Screenshot),
                dialogPreferencesChanged: RecognitionDialogPreferencesChanged);
        }
    }
}

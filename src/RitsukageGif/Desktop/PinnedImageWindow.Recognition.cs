using Avalonia.Controls;
using Avalonia.Input.Platform;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        internal Func<Window, ReadOnlyMemory<byte>, CancellationToken, Task>? ImageTranslationRequested { get; set; }
        internal Func<TranslationPreferences> TranslationPreferencesProvider { get; set; } = () => new();
        internal Action<Window, IReadOnlyList<string>>? PinFormulaRequested { get; set; }
        internal Action<FormulaCopyFormat>? FormulaCopyFormatChanged { get; set; }
        internal Action<bool>? RecognitionOriginalImageVisibilityChanged { get; set; }
        internal Action<FormulaLatexFormat>? FormulaLatexFormatChanged { get; set; }

        private void AddRecognitionMenu(ContextMenu menu)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(Command("RecognizeText", () => RecognizeAsync(RecognitionMode.Text)));
            menu.Items.Add(Command("RecognizeBarcode", () => RecognizeAsync(RecognitionMode.Barcode)));
            menu.Items.Add(Command("TableRecognition", () => RecognizeAsync(RecognitionMode.Table)));
            menu.Items.Add(Command("FormulaRecognition", () => RecognizeAsync(RecognitionMode.Formula)));
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
        }

        private async Task RecognizeAsync(RecognitionMode mode)
        {
            var output = await CaptureOutputAsync(OutputColorMode.Sdr, _lifetime.Token);
            var png = output.Png;
            var preferences = RecognitionPreferencesProvider().Normalize();
            if (mode == RecognitionMode.Text && !preferences.ShowDialogOnRecognition)
            {
                var text = await RecognitionOutput.RecognizeAsync(png, preferences,
                    TextRecognitionServiceProvider?.Invoke() ?? DefaultTextRecognition, _lifetime.Token);
                if (string.IsNullOrEmpty(text)) throw new InvalidOperationException(UiText.Get("RecognitionEmpty"));
                var clipboard = Clipboard ?? throw new InvalidOperationException(UiText.Get("ClipboardFailed"));
                await clipboard.SetTextAsync(text);
                await clipboard.FlushAsync();
                await SaveAutomaticallyAsync(ScreenshotAutoSaveTriggers.Recognize, output);
                return;
            }

            await RecognitionWindow.ShowAsync(this, png, mode,
                _ => SaveAutomaticallyAsync(ScreenshotAutoSaveTriggers.Recognize, output), preferences,
                token: _lifetime.Token, translationPreferences: TranslationPreferencesProvider(),
                pinFormula: PinFormulaRequested,
                copyFormatChanged: FormulaCopyFormatChanged,
                originalImageVisibilityChanged: RecognitionOriginalImageVisibilityChanged,
                latexFormatChanged: FormulaLatexFormatChanged);
        }
    }
}

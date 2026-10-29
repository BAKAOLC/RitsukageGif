using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        private void AddRecognitionMenu(ContextMenu menu)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(Command("RecognizeText", () => RecognizeAsync(RecognitionMode.Text)));
            menu.Items.Add(Command("RecognizeBarcode", () => RecognizeAsync(RecognitionMode.Barcode)));
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
                token: _lifetime.Token);
        }
    }
}

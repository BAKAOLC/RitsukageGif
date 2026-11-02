using Avalonia.Controls;
using Avalonia.Input.Platform;

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private async Task<bool> CopyActionScreenshotCodeTextAsync(Window? owner, ScreenshotResult result,
            CancellationToken token)
        {
            if (result.Action != ScreenshotOutputAction.Copy) return await OutputScreenshotAsync(owner, result, token);
            var codes = await new BarcodeRecognitionService().RecognizeAsync(result.Png, token);
            token.ThrowIfCancellationRequested();
            var text = CodeRecognitionOutput.FirstText(codes);
            if (text is null)
            {
                _screenshotSelection?.CompleteCodeWithoutResult();
                return false;
            }

            var clipboard = (owner ?? _services.Context).Clipboard ??
                            throw new InvalidOperationException(UiText.Get("ClipboardFailed"));
            await clipboard.SetTextAsync(text);
            await clipboard.FlushAsync();
            return true;
        }
    }
}

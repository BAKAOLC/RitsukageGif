using Avalonia.Controls;
using Avalonia.Input.Platform;

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private readonly HashSet<Task> _translationTasks = [];

        private async Task TranslateClipboardAsync()
        {
            var clipboard = _services.Context.Clipboard ??
                            throw new InvalidOperationException(UiText.Get("ClipboardFailed"));
            var text = await clipboard.TryGetTextAsync();
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException(UiText.Get("GlobalActionClipboardEmpty"));
            await ShowTranslationAsync(View is { IsVisible: true } ? View : _services.Context, text,
                _cleanupCancellation.Token);
        }

        private async Task<bool> TranslateActionScreenshotAsync(Window? owner, ScreenshotResult result,
            CancellationToken token)
        {
            if (result.Action != ScreenshotOutputAction.Copy) return await OutputScreenshotAsync(owner, result, token);
            var text = await RecognitionOutput.RecognizeAsync(result.Png, Settings.Recognition,
                _pinnedImages.TextRecognitionService, token);
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException(UiText.Get("RecognitionEmpty"));
            await ShowTranslationAsync(owner ?? _services.Context, text, token);
            return true;
        }

        private async Task ShowTranslationAsync(Window owner, string text, CancellationToken token)
        {
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, _cleanupCancellation.Token);
            var task = TranslationWindow.ShowAsync(owner, text, Settings.Translation with { }, true, lifetime.Token);
            _translationTasks.Add(task);
            try
            {
                await task;
            }
            finally
            {
                _translationTasks.Remove(task);
            }
        }
    }
}

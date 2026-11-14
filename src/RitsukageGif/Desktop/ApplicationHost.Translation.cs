using Avalonia.Controls;
using Avalonia.Input.Platform;

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private readonly HashSet<Task> _translationTasks = [];

        private async Task TranslateClipboardAsync(CancellationToken token)
        {
            var clipboard = _services.Context.Clipboard ??
                            throw new InvalidOperationException(UiText.Get("ClipboardFailed"));
            var text = await clipboard.TryGetTextAsync().WaitAsync(token);
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException(UiText.Get("GlobalActionClipboardEmpty"));
            await ShowTranslationAsync(View is { IsVisible: true } ? View : _services.Context, text, token);
        }

        private async Task OpenScreenshotTranslationAsync(ReadOnlyMemory<byte> png)
        {
            try
            {
                await ShowImageTranslationAsync(_services.Context, png, _hostCancellation.Token);
            }
            catch (OperationCanceledException) when (_hostCancellation.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                HostLogger.ZLogWarning(
                    $"Screenshot translation window failed: exceptionType={error.GetType().Name}, hresult={error.HResult}");
                if (!IsClosing) SetStatus(UiText.Get("TranslationFailed"));
            }
        }

        private async Task PinScreenshotTranslationAsync(ScreenshotResult result, ScreenshotInvocation invocation)
        {
            using var work = CancellationTokenSource.CreateLinkedTokenSource(_hostCancellation.Token,
                invocation.CommandToken);
            try
            {
                if (result.ColorMode != OutputColorMode.Sdr && result.Hdr is not { ColorInfo.IsHdrSource: true })
                    throw new NotSupportedException(result.HdrUnavailableReason ?? UiText.Get("HdrCaptureUnavailable"));
                result = await ScreenshotOutputDecoration.PrepareAsync(result, work.Token);
                var pin = await PinScreenshotAsync(result, work.Token);
                work.Token.ThrowIfCancellationRequested();
                pin?.StartScreenshotTranslation();
            }
            catch (Exception error) when (!invocation.PropagateErrors &&
                                          (error is not OperationCanceledException || !work.IsCancellationRequested))
            {
                HostLogger.ZLogWarning(error, $"Screenshot translation pin failed.");
                if (!IsClosing) SetStatus(error.Message);
            }
        }

        private Task ShowImageTranslationAsync(Window owner, ReadOnlyMemory<byte> png, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, _hostCancellation.Token);
            var baseline = Settings.Translation with { };
            var task = ImageTranslationWindow.ShowAsync(owner, png, baseline, Settings.Recognition with { },
                _pinnedImages.TextRecognitionService, true, edited =>
                {
                    var current = Settings;
                    var updated = current.MergeChanges(current with { Translation = baseline },
                        current with { Translation = edited });
                    Settings = updated;
                    QueueSettingsSave(updated);
                    baseline = edited with { };
                }, lifetime.Token);
            _translationTasks.Add(task);
            _ = ObserveImageTranslationAsync(task, lifetime);
            return Task.CompletedTask;
        }

        private async Task ObserveImageTranslationAsync(Task task, CancellationTokenSource lifetime)
        {
            try
            {
                await task;
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                HostLogger.ZLogWarning(
                    $"Image translation window failed: exceptionType={error.GetType().Name}, hresult={error.HResult}");
                if (!IsClosing) SetStatus(UiText.Get("TranslationFailed"));
            }
            finally
            {
                _translationTasks.Remove(task);
                lifetime.Dispose();
            }
        }

        private async Task ShowTranslationAsync(Window owner, string text, CancellationToken token)
        {
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, _hostCancellation.Token);
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

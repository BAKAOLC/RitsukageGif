using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private CaptureSourceKind ActionScreenshotSource(GlobalActionBinding action)
        {
            return action.Parameter switch
            {
                "screen" => CaptureSourceKind.Screen,
                "window" => CaptureSourceKind.Window,
                _ => Settings.CaptureSource,
            };
        }

        private async Task ExecuteScreenshotActionAsync(GlobalActionBinding action, PixelRect? requestedRegion = null)
        {
            var source = ActionScreenshotSource(action);
            var output = GlobalActions.ScreenshotOutput(action);
            var region = requestedRegion;
            WindowTarget? target = null;
            // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
            switch (action.Kind)
            {
                case GlobalActionKind.CaptureActiveWindow:
                    if (!OperatingSystem.IsWindows())
                        throw new PlatformNotSupportedException(UiText.Get("GlobalActionActiveWindowUnavailable"));
                    var foreground = ActionForegroundWindow();
                    if (foreground == 0) throw new InvalidOperationException(UiText.Get("WindowNotFound"));
                    target = await Task.Run(() =>
                        WindowLocator.List().FirstOrDefault(window => window.Handle == foreground));
                    if (target is null) throw new InvalidOperationException(UiText.Get("WindowNotFound"));
                    region = source == CaptureSourceKind.Window ? target.Client : target.Bounds;
                    break;
                case GlobalActionKind.CaptureLastRegion:
                    if (DesktopCapture.UsesSourceSelection)
                        throw new PlatformNotSupportedException(UiText.Get("GlobalActionDirectCaptureUnavailable"));
                    if (_lastScreenshotSelection is not { Region.IsEmpty: false } previous)
                        throw new InvalidOperationException(UiText.Get("GlobalActionLastRegionMissing"));
                    region = previous.Region;
                    if (source == CaptureSourceKind.Window)
                    {
                        target = previous.Target is { } previousTarget
                            ? await Task.Run(() => WindowLocator.Refresh(previousTarget))
                            : null;
                        if (target is null) throw new InvalidOperationException(UiText.Get("WindowNotFound"));
                        region = previous.Region.Intersect(target.Client);
                    }

                    break;
            }

            if (GlobalActions.IsDirectScreenshot(action.Kind) && region is not { IsEmpty: false })
                throw new InvalidOperationException(UiText.Get("GlobalActionLastRegionMissing"));

            var confirm = output switch
            {
                ActionScreenshotOutput.Copy or ActionScreenshotOutput.CopyText => ScreenshotOutputAction.Copy,
                ActionScreenshotOutput.Pin => ScreenshotOutputAction.Pin,
                ActionScreenshotOutput.Save or ActionScreenshotOutput.QuickSave => ScreenshotOutputAction.Save,
                _ => throw new ArgumentOutOfRangeException(nameof(action)),
            };
            Func<Window?, ScreenshotResult, CancellationToken, Task<bool>>? handler = null;
            switch (output)
            {
                case ActionScreenshotOutput.CopyText:
                    handler = CopyActionScreenshotTextAsync;
                    break;
                case ActionScreenshotOutput.QuickSave:
                    var directory = action.Arguments.GetValueOrDefault("directory");
                    directory = Path.GetFullPath(
                        string.IsNullOrWhiteSpace(directory)
                            ? Settings.ScreenshotSave.Quick.ResolveDirectory(Settings.OutputDirectory)
                            : directory);
                    handler = (owner, result, token) => QuickSaveActionScreenshotAsync(owner, result, directory, token);
                    break;
                case ActionScreenshotOutput.Copy:
                case ActionScreenshotOutput.Pin:
                case ActionScreenshotOutput.Save:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(action));
            }

            await ScreenshotAsync(source, region, confirm, action.Kind != GlobalActionKind.Screenshot,
                target, handler, action.Kind == GlobalActionKind.CaptureActiveWindow,
                GlobalActions.ScreenshotDelay(action), action.Kind == GlobalActionKind.ScreenshotAndScroll,
                output == ActionScreenshotOutput.CopyText);
        }

        private async Task<bool> CopyActionScreenshotTextAsync(Window? owner, ScreenshotResult result,
            CancellationToken token)
        {
            if (result.Action != ScreenshotOutputAction.Copy) return await OutputScreenshotAsync(owner, result, token);
            var text = await RecognitionOutput.RecognizeAsync(result.Png, Settings.Recognition,
                _pinnedImages.TextRecognitionService, token);
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException(UiText.Get("RecognitionEmpty"));
            var clipboard = (owner ?? _services.Context).Clipboard ??
                            throw new InvalidOperationException(UiText.Get("ClipboardFailed"));
            await clipboard.SetTextAsync(text);
            token.ThrowIfCancellationRequested();
            if (Settings.Recognition.ShowDialogOnCopyAll && owner is not null)
                await RecognitionWindow.ShowAsync(owner, result.Png, RecognitionMode.Text,
                    preferences: Settings.Recognition, initialText: text, token: token);
            return true;
        }

        private async Task<bool> QuickSaveActionScreenshotAsync(Window? owner, ScreenshotResult result,
            string directory, CancellationToken token)
        {
            if (result.Action != ScreenshotOutputAction.Save) return await OutputScreenshotAsync(owner, result, token);
            token.ThrowIfCancellationRequested();
            var preferences = Settings.ScreenshotSave.Normalize().Quick;
            var format = result.ColorMode == OutputColorMode.Sdr ? preferences.Format : StaticImageFormat.Png;
            var path = Path.Combine(directory, preferences.CreateFileName(ScreenshotName(result), format));
            return await SaveScreenshotAsync(result, path, preferences, false, false, token);
        }

        private async Task PinActionClipboardAsync()
        {
            var clipboard = _services.Context.Clipboard ??
                            throw new InvalidOperationException(UiText.Get("ClipboardFailed"));
            if (!await _pinnedImages.PinClipboardAsync(clipboard)) SetStatus(UiText.Get("GlobalActionClipboardEmpty"));
        }

        [LibraryImport("user32.dll", EntryPoint = "GetForegroundWindow")]
        private static partial nint ActionForegroundWindow();
    }
}

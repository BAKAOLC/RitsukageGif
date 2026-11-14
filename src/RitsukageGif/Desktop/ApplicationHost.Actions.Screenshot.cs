using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Threading;

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

        private async Task<ScreenshotInvocation> ExecuteScreenshotActionAsync(GlobalActionBinding action,
            PixelRect? requestedRegion = null,
            bool propagateErrors = false, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            var source = ActionScreenshotSource(action);
            var output = GlobalActions.ScreenshotOutput(action);
            var region = requestedRegion;
            WindowTarget? target = null;
            // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
            switch (action.Kind)
            {
                case GlobalActionKind.CaptureActiveWindow:
                    if (!WindowLocator.CanFindActiveWindow)
                        throw new PlatformNotSupportedException(UiText.Get("GlobalActionActiveWindowUnavailable"));
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
                            ? await Task.Run(() => WindowLocator.Refresh(previousTarget), token).WaitAsync(token)
                            : null;
                        if (target is null) throw new InvalidOperationException(UiText.Get("WindowNotFound"));
                        region = previous.Region.Intersect(target.Client);
                    }

                    break;
            }

            if (GlobalActions.IsDirectScreenshot(action.Kind) && action.Kind != GlobalActionKind.CaptureActiveWindow &&
                region is not { IsEmpty: false })
                throw new InvalidOperationException(UiText.Get("GlobalActionLastRegionMissing"));

            var confirm = output switch
            {
                ActionScreenshotOutput.Copy or ActionScreenshotOutput.CopyText or ActionScreenshotOutput.Translate
                    or ActionScreenshotOutput.CopyCodeText
                    or ActionScreenshotOutput.RecognizeText or ActionScreenshotOutput.RecognizeTable
                    or ActionScreenshotOutput.RecognizeFormula =>
                    ScreenshotOutputAction.Copy,
                ActionScreenshotOutput.Pin => ScreenshotOutputAction.Pin,
                ActionScreenshotOutput.Save => ScreenshotOutputAction.Save,
                ActionScreenshotOutput.QuickSave => ScreenshotOutputAction.QuickSave,
                _ => throw new ArgumentOutOfRangeException(nameof(action)),
            };
            Func<Window?, ScreenshotResult, CancellationToken, Task<bool>>? handler = null;
            switch (output)
            {
                case ActionScreenshotOutput.CopyText:
                    handler = CopyActionScreenshotTextAsync;
                    break;
                case ActionScreenshotOutput.CopyCodeText:
                    handler = CopyActionScreenshotCodeTextAsync;
                    break;
                case ActionScreenshotOutput.Translate:
                    break;
                case ActionScreenshotOutput.RecognizeText:
                case ActionScreenshotOutput.RecognizeTable:
                case ActionScreenshotOutput.RecognizeFormula:
                    var recognitionMode = output switch
                    {
                        ActionScreenshotOutput.RecognizeTable => RecognitionMode.Table,
                        ActionScreenshotOutput.RecognizeFormula => RecognitionMode.Formula,
                        _ => RecognitionMode.Text,
                    };
                    handler = (owner, result, captureToken) =>
                        RecognizeActionScreenshotAsync(owner, result, recognitionMode, captureToken);
                    break;
                case ActionScreenshotOutput.QuickSave:
                    var directory = action.Arguments.GetValueOrDefault("directory");
                    if (string.IsNullOrWhiteSpace(directory))
                        directory = string.IsNullOrWhiteSpace(Settings.ScreenshotSave.Quick.Directory)
                            ? Settings.OutputDirectory
                            : Settings.ScreenshotSave.Quick.Directory;
                    handler = (owner, result, captureToken) =>
                        QuickSaveActionScreenshotAsync(owner, result, directory, captureToken);
                    break;
                case ActionScreenshotOutput.Copy:
                case ActionScreenshotOutput.Pin:
                case ActionScreenshotOutput.Save:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(action));
            }

            token.ThrowIfCancellationRequested();
            var deferCopyOutput = output is ActionScreenshotOutput.CopyText or ActionScreenshotOutput.RecognizeText or
                ActionScreenshotOutput.RecognizeTable or ActionScreenshotOutput.RecognizeFormula;
            var invocation = new ScreenshotInvocation(propagateErrors)
            {
                DeferCopyOutput = deferCopyOutput, CommandToken = token,
            };
            var operation = ScreenshotAsync(source, region, confirm, GlobalActions.Definition(action.Kind).AutoConfirm,
                target, handler, action.Kind == GlobalActionKind.CaptureActiveWindow,
                GlobalActions.ScreenshotDelay(action), GlobalActions.Definition(action.Kind).StartWithScrolling,
                output is ActionScreenshotOutput.CopyText or ActionScreenshotOutput.CopyCodeText,
                output == ActionScreenshotOutput.Translate,
                GlobalActions.ScreenshotGeometry(action), action.Kind == GlobalActionKind.CaptureActiveWindow,
                GlobalActions.Definition(action.Kind).StartWithRecording, invocation);
            var cancellation = _screenshotCancellation;
            await using var registration = token.Register(() => Dispatcher.UIThread.Post(() =>
            {
                if (cancellation is not null && ReferenceEquals(_screenshotCancellation, cancellation))
                    CancelScreenshot();
            }));
            return await operation;
        }

        private async Task<bool> RecognizeActionScreenshotAsync(Window? owner, ScreenshotResult result,
            RecognitionMode mode, CancellationToken token)
        {
            if (result.Action != ScreenshotOutputAction.Copy) return await OutputScreenshotAsync(owner, result, token);
            token.ThrowIfCancellationRequested();
            await RecognitionWindow.ShowAsync(owner ?? _services.Context, result.Png, mode,
                preferences: Settings.Recognition, token: token, translationPreferences: Settings.Translation,
                requireResult: _screenshotInvocation.Value?.PropagateErrors == true,
                copyFormatChanged: PersistFormulaCopyFormat,
                originalImageVisibilityChanged: PersistRecognitionOriginalImage,
                latexFormatChanged: PersistFormulaLatexFormat,
                imageSaveQuality: () => Settings.ScreenshotSave.Quality,
                copyDirectlyNextTime: () => DisableRecognitionConfirmation(RecognitionConfirmationKind.Screenshot),
                dialogPreferencesChanged: PersistRecognitionDialog);
            return true;
        }

        private async Task RunScreenshotRecognitionAsync(ScreenshotRecognitionRequest request,
            Func<ScreenshotResult, ScreenshotAutoSaveTriggers, CancellationToken, Task>? output, bool requireResult,
            CancellationToken token)
        {
            var settings = Settings;
            var preferences = settings.Recognition;
            var screenshot = request.Screenshot;
            if (request.Mode == RecognitionMode.Text && !preferences.ShowDialogOnRecognition)
            {
                var recognition = await RecognitionOutput.RecognizeAsync(screenshot.Png, preferences,
                    _pinnedImages.TextRecognitionService, token);
                var text = RecognitionTextFormatter.Format(recognition, preferences);
                token.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(text))
                    throw new InvalidOperationException(UiText.Get("RecognitionEmpty"));

                var clipboard = _services.Context.Clipboard ??
                                throw new InvalidOperationException(UiText.Get("ClipboardFailed"));
                await clipboard.SetTextAsync(text);
                await clipboard.FlushAsync();
                token.ThrowIfCancellationRequested();
                PlayScreenshotSound(ScreenshotSoundTiming.Copy);
                if (output is not null) await output(screenshot, ScreenshotAutoSaveTriggers.Recognize, token);
                return;
            }

            await RecognitionWindow.ShowAsync(_services.Context, screenshot.Png, request.Mode,
                output is null
                    ? null
                    : workToken => output(screenshot, ScreenshotAutoSaveTriggers.Recognize, workToken),
                preferences,
                translationPreferences: settings.Translation, copyFormatChanged: PersistFormulaCopyFormat,
                originalImageVisibilityChanged: PersistRecognitionOriginalImage,
                latexFormatChanged: PersistFormulaLatexFormat,
                copyDirectlyNextTime: () => DisableRecognitionConfirmation(RecognitionConfirmationKind.Screenshot),
                dialogPreferencesChanged: PersistRecognitionDialog,
                imageSaveQuality: () => Settings.ScreenshotSave.Quality,
                requireResult: requireResult, token: token);
        }

        private async Task<bool> CopyActionScreenshotTextAsync(Window? owner, ScreenshotResult result,
            CancellationToken token)
        {
            if (result.Action != ScreenshotOutputAction.Copy) return await OutputScreenshotAsync(owner, result, token);
            var recognition = await RecognitionOutput.RecognizeAsync(result.Png, Settings.Recognition,
                _pinnedImages.TextRecognitionService, token);
            var text = RecognitionTextFormatter.Format(recognition, Settings.Recognition);
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException(UiText.Get("RecognitionEmpty"));
            if (Settings.Recognition.ShowDialogOnRecognition)
            {
                await RecognitionWindow.ShowAsync(owner, result.Png, RecognitionMode.Text,
                    preferences: Settings.Recognition, initialResult: recognition, token: token,
                    requireResult: _screenshotInvocation.Value?.PropagateErrors == true,
                    translationPreferences: Settings.Translation,
                    originalImageVisibilityChanged: PersistRecognitionOriginalImage,
                    copyDirectlyNextTime: () => DisableRecognitionConfirmation(RecognitionConfirmationKind.Screenshot),
                    dialogPreferencesChanged: PersistRecognitionDialog);
            }
            else
            {
                var clipboard = (owner ?? _services.Context).Clipboard ??
                                throw new InvalidOperationException(UiText.Get("ClipboardFailed"));
                await clipboard.SetTextAsync(text);
                await clipboard.FlushAsync();
                token.ThrowIfCancellationRequested();
            }

            return true;
        }

        private async Task<bool> QuickSaveActionScreenshotAsync(Window? owner, ScreenshotResult result,
            string directory, CancellationToken token)
        {
            if (result.Action != ScreenshotOutputAction.QuickSave)
                return await OutputScreenshotAsync(owner, result, token);
            token.ThrowIfCancellationRequested();
            var preferences = ScaleScreenshotSave(result, ScreenshotSaveForOutput.Normalize().Quick);
            var format = result.ColorMode == OutputColorMode.Sdr ? preferences.Format : StaticImageFormat.Png;
            var path = (preferences with { Directory = directory }).CreatePath(Settings.OutputDirectory,
                ScreenshotName(result, preferences.OutputSize), format);
            return await SaveScreenshotAsync(result, path, preferences, false, false, token,
                owner as ScreenshotScrollingWindow);
        }

        private async Task PinActionClipboardAsync(CancellationToken token)
        {
            var clipboard = _services.Context.Clipboard ??
                            throw new InvalidOperationException(UiText.Get("ClipboardFailed"));
            if (!await _pinnedImages.PinClipboardAsync(clipboard, token))
                SetStatus(UiText.Get("GlobalActionClipboardEmpty"));
        }
    }
}

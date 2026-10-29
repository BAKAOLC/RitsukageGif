using System.Buffers.Binary;
using System.Diagnostics;
using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private SelectionResult? _lastScreenshotSelection;
        private CancellationTokenSource? _screenshotCancellation;
        private Task? _screenshotOperation;
        private SelectionCoordinator? _screenshotSelection;

        private bool CanTakeScreenshot => !IsClosing && !IsSelecting && !IsConfiguring && _screenshotOperation is null;
        private bool IsTakingScreenshot => _screenshotOperation is not null;

        private Task ScreenshotAsync()
        {
            return ScreenshotAsync(Settings.CaptureSource);
        }

        private Task ScreenshotAsync(CaptureSourceKind source, PixelRect? initialRegion = null,
            ScreenshotOutputAction? confirmAction = null, bool autoConfirm = false,
            WindowTarget? initialTarget = null,
            Func<Window?, ScreenshotResult, CancellationToken, Task<bool>>? outputHandler = null,
            bool wholeInitialTarget = false, TimeSpan delay = default, bool startWithScrolling = false,
            bool copyTextOutput = false)
        {
            if (!CanTakeScreenshot) return Task.CompletedTask;
            if (!Enum.IsDefined(source)) throw new ArgumentOutOfRangeException(nameof(source));
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _screenshotOperation = completion.Task;
            _ = CompleteScreenshotAsync(completion, RunScreenshotAsync(source, initialRegion, confirmAction,
                autoConfirm, initialTarget, outputHandler, wholeInitialTarget, delay, startWithScrolling,
                copyTextOutput));
            return completion.Task;
        }

        private async Task CompleteScreenshotAsync(TaskCompletionSource completion, Task operation)
        {
            try
            {
                await operation;
                completion.TrySetResult();
            }
            catch (Exception error)
            {
                completion.TrySetException(error);
            }
            finally
            {
                if (ReferenceEquals(_screenshotOperation, completion.Task)) _screenshotOperation = null;
            }
        }

        private async Task RunScreenshotAsync(CaptureSourceKind source, PixelRect? initialRegion,
            ScreenshotOutputAction? confirmAction, bool autoConfirm, WindowTarget? initialTarget,
            Func<Window?, ScreenshotResult, CancellationToken, Task<bool>>? outputHandler,
            bool wholeInitialTarget, TimeSpan delay, bool startWithScrolling, bool copyTextOutput)
        {
            var operationId = Interlocked.Increment(ref _nextOperationId);
            using var scope = HostLogger.BeginScope("ScreenshotOperation {ScreenshotOperationId}", operationId);
            var started = Stopwatch.GetTimestamp();
            HostLogger.ZLogInformation(
                $"Screenshot operation started. OperationId={operationId} Source={source.ToString()} InitialRegion={initialRegion} DelayMilliseconds={delay.TotalMilliseconds} AutoConfirm={autoConfirm} StartWithScrolling={startWithScrolling}");
            using var cancellation = new CancellationTokenSource();
            _screenshotCancellation = cancellation;
            IsSelecting = true;
            try
            {
                var operation = CaptureScreenshotAsync(source, cancellation.Token, initialRegion, confirmAction,
                    autoConfirm, initialTarget, outputHandler, wholeInitialTarget, delay, startWithScrolling,
                    copyTextOutput);
                Notify();
                await operation;
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                HostLogger.ZLogDebug($"Screenshot operation cancelled. OperationId={operationId} Closing={IsClosing}");
            }
            catch (Exception error) when (!IsClosing)
            {
                HostLogger.ZLogError(error, $"Screenshot operation failed. OperationId={operationId}");
                SetStatus(error.Message);
            }
            finally
            {
                _screenshotSelection = null;
                _screenshotCancellation = null;
                _screenshotOperation = null;
                IsSelecting = false;
                Notify();
                HostLogger.ZLogInformation(
                    $"Screenshot operation ended. OperationId={operationId} ElapsedMilliseconds={Stopwatch.GetElapsedTime(started).TotalMilliseconds} Closing={IsClosing}");
            }
        }

        private void CancelScreenshot()
        {
            HostLogger.ZLogDebug(
                $"Screenshot cancellation requested. HasSelection={_screenshotSelection is not null} HasCancellationSource={_screenshotCancellation is not null}");
            try
            {
                _screenshotCancellation?.Cancel();
            }
            finally
            {
                _screenshotSelection?.Cancel();
            }
        }

        private async Task CaptureScreenshotAsync(CaptureSourceKind source, CancellationToken token,
            PixelRect? initialRegion = null, ScreenshotOutputAction? confirmAction = null,
            bool autoConfirm = false, WindowTarget? initialTarget = null,
            Func<Window?, ScreenshotResult, CancellationToken, Task<bool>>? outputHandler = null,
            bool wholeInitialTarget = false, TimeSpan delay = default, bool startWithScrolling = false,
            bool copyTextOutput = false)
        {
            var previousView = View;
            var wasVisible = previousView?.IsVisible == true;
            using var settingsVisibility = await CaptureWindowVisibilityLease.HideAsync(_settingsWindow,
                () => !IsClosing, token);
            var pasteTarget = Settings.ScreenshotClipboard.PasteAfterCopy && !copyTextOutput && outputHandler is null
                ? await ScreenshotPasteGuard.CaptureTargetAsync(token)
                : null;
            uint copiedSequence = 0;
            try
            {
                HostLogger.ZLogDebug($"Screenshot waiting for application windows to hide.");
                await CaptureWindowVisibility.HideAsync(previousView, token);
                if (delay > TimeSpan.Zero) await Task.Delay(delay, token);
                token.ThrowIfCancellationRequested();
                var action = confirmAction ?? ScreenshotOutputAction.Copy;
                using var selection = new SelectionCoordinator(Screens, Settings.SnapRadius,
                    Settings.EdgeThreshold, source == CaptureSourceKind.Window, Settings.Cursor,
                    retainScreenshot: true, retainHdr: true,
                    screenshotToneMap: Settings.CaptureToneMap,
                    screenshotColorMode: Settings.CaptureColorMode)
                {
                    InitialRegion = initialRegion, ConfirmAction = action, AutoConfirm = autoConfirm,
                    InitialTarget = initialTarget, WholeInitialTarget = wholeInitialTarget,
                    StartWithScrolling = startWithScrolling,
                };
                _screenshotSelection = selection;
                HostLogger.ZLogDebug($"Screenshot selection coordinator created. Source={source.ToString()}");
                ConfigureSelectionPreferences(selection);
                // ReSharper disable AccessToDisposedClosure
                selection.RecognitionOutputHandler = async (result, cancellationToken) =>
                {
                    _captureHistory.RecordScreenshot(result.Png, result.OutputRegion ?? selection.Snapshot.Region,
                        selection.WindowSource ? CaptureSourceKind.Window : CaptureSourceKind.Screen, result.Hdr,
                        result.CaptureScaling, result.Decoration, result.OutputOrigin);
                    var trigger = ScreenshotAutoSaveTriggers.Recognize;
                    if (selection.WasScrollingScreenshot) trigger |= ScreenshotAutoSaveTriggers.Scrolling;
                    try
                    {
                        await AutoSaveScreenshotAsync(result, trigger, cancellationToken);
                    }
                    catch (Exception error) when (error is not OperationCanceledException ||
                                                  !cancellationToken.IsCancellationRequested)
                    {
                        HostLogger.ZLogError(error, $"Screenshot recognition automatic save failed.");
                        SetStatus(error.Message);
                    }
                };
                selection.ScreenshotOutputHandler = async (owner, result, cancellationToken) =>
                {
                    var raw = result with { CaptureScaling = selection.ScreenshotCaptureScaling };
                    result = copyTextOutput
                        ? raw
                        : await ScreenshotOutputDecoration.PrepareAsync(raw, cancellationToken);
                    var success = await (outputHandler ?? OutputScreenshotAsync)(owner, result, cancellationToken);
                    if (!success) return false;
                    if (pasteTarget is not null && result.Action == ScreenshotOutputAction.Copy)
                        copiedSequence = ScreenshotPasteGuard.ClipboardSequence;
                    _captureHistory.RecordScreenshot(raw.Png, raw.OutputRegion ?? selection.Snapshot.Region,
                        selection.WindowSource ? CaptureSourceKind.Window : CaptureSourceKind.Screen, raw.Hdr,
                        raw.CaptureScaling, raw.Decoration, raw.OutputOrigin);
                    var trigger = result.Action switch
                    {
                        ScreenshotOutputAction.Copy => ScreenshotAutoSaveTriggers.Copy,
                        ScreenshotOutputAction.Pin => ScreenshotAutoSaveTriggers.Pin,
                        ScreenshotOutputAction.Save or ScreenshotOutputAction.QuickSave => ScreenshotAutoSaveTriggers
                            .Save,
                        _ => ScreenshotAutoSaveTriggers.None,
                    };
                    if (copyTextOutput && result.Action == ScreenshotOutputAction.Copy)
                        trigger = ScreenshotAutoSaveTriggers.Recognize;
                    if (selection.WasScrollingScreenshot) trigger |= ScreenshotAutoSaveTriggers.Scrolling;
                    try
                    {
                        await AutoSaveScreenshotAsync(result, trigger, cancellationToken);
                    }
                    catch (Exception error)
                    {
                        SetStatus(error.Message);
                    }

                    return true;
                };
                // ReSharper restore AccessToDisposedClosure
                HostLogger.ZLogInformation($"Screenshot selection initialization requested.");
                if (await selection.ShowAsync() is { } completed)
                {
                    _lastScreenshotSelection = completed;
                    HostLogger.ZLogInformation($"Screenshot selection accepted. Region={completed.Region}");
                }
                else
                {
                    HostLogger.ZLogDebug($"Screenshot selection returned without an accepted result.");
                }

                RememberAnnotationPreferences(selection);
                RememberFrameDecorationPreferences(selection);
                token.ThrowIfCancellationRequested();
                if (copiedSequence != 0 && !IsClosing)
                    await ScreenshotPasteGuard.TryPasteAsync(pasteTarget, copiedSequence, token);
            }
            finally
            {
                if (!IsClosing && wasVisible && previousView is not null)
                {
                    var activate = previousView.ShowActivated;
                    previousView.ShowActivated = false;
                    try
                    {
                        previousView.Show();
                    }
                    finally
                    {
                        previousView.ShowActivated = activate;
                    }
                }
            }
        }

        private async Task<bool> OutputScreenshotAsync(Window? owner, ScreenshotResult result,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var mode = result.ColorMode;
            var sdrPng = result.Png;
            if (mode != OutputColorMode.Sdr && result.Hdr is not { ColorInfo.IsHdrSource: true })
                throw new NotSupportedException(result.HdrUnavailableReason ?? UiText.Get("HdrCaptureUnavailable"));
            var context = owner ?? _services.Context;
            switch (result.Action)
            {
                case ScreenshotOutputAction.Pin:
                    if (mode == OutputColorMode.Sdr)
                        await _pinnedImages.PinAsync(sdrPng, result.CaptureScaling, result.OutputOrigin,
                            result.ContentOffset, token);
                    else
                        await _pinnedImages.PinHdrAsync(sdrPng, result.Hdr!, result.CaptureScaling, result.OutputOrigin,
                            result.ContentOffset, token);
                    return true;
                case ScreenshotOutputAction.Copy:
                    await CopyScreenshotAsync(result, token);
                    return true;
                case ScreenshotOutputAction.QuickSave:
                {
                    var quickProfile = Settings.ScreenshotSave.Normalize().Quick;
                    var format = mode == OutputColorMode.Sdr ? quickProfile.Format : StaticImageFormat.Png;
                    var quickPath = Path.Combine(quickProfile.ResolveDirectory(Settings.OutputDirectory),
                        quickProfile.CreateFileName(ScreenshotName(result, quickProfile.OutputSize), format));
                    return await SaveScreenshotAsync(result, quickPath, quickProfile, false, false, token);
                }
                case ScreenshotOutputAction.Save:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(result));
            }

            var profile = Settings.ScreenshotSave.ManualProfile();
            var path = await ScreenshotOutputStorage.PickPathAsync(owner, context.StorageProvider,
                profile, Settings.OutputDirectory, "Screenshot", mode, Settings.Appearance.SaveDialogStyle,
                ScreenshotName(result, profile.OutputSize), token);
            return path is not null && await SaveScreenshotAsync(result, path, profile, true, false, token);
        }

        private async Task<bool> SaveScreenshotAsync(ScreenshotResult result, string path,
            ScreenshotSaveProfile profile, bool rememberExtension, bool automatic, CancellationToken token)
        {
            var output = await ScreenshotImageStorage.SaveAsync(result, path, profile, token);
            LastScreenshotOutput = output;
            SetStatus(UiText.Format("AnimationExported", string.Join(", ", output.Paths)));
            if (rememberExtension)
                try
                {
                    PersistScreenshotExtension(path);
                }
                catch (Exception error)
                {
                    SetStatus(error.Message);
                }

            try
            {
                await ScreenshotSaveCompletion.RunAsync(output.Paths, profile, _services.Context.Clipboard,
                    _services.Context.StorageProvider, () => CopyScreenshotAsync(result, CancellationToken.None),
                    ShowOutputNotification, CancellationToken.None);
            }
            catch (Exception error)
            {
                SetStatus(error.Message);
            }
            finally
            {
                if (automatic)
                    QueueCleanup(output.Paths,
                        profile.AfterSave == ScreenshotAfterSaveAction.CopyFile ? output.Paths : null);
            }

            return true;
        }

        private void PersistScreenshotExtension(string path)
        {
            var preferences = Settings.ScreenshotSave;
            var extension = ScreenshotSaveProfile.NormalizeExtension(Path.GetExtension(path));
            if (!preferences.RememberExtension || extension is null || extension == preferences.LastExtension) return;
            var updated = Settings with { ScreenshotSave = preferences with { LastExtension = extension } };
            QueueSettingsSave(updated);
            Settings = updated;
        }

        private ScreenshotNameContext ScreenshotName(ScreenshotResult result, ScreenshotOutputSize? outputSize = null)
        {
            var snapshot = _screenshotSelection?.Snapshot;
            var width = result.Hdr?.Width ?? 0;
            var height = result.Hdr?.Height ?? 0;
            if (result.Png.Length >= 24 &&
                result.Png.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            {
                width = BinaryPrimitives.ReadInt32BigEndian(result.Png.AsSpan(16, 4));
                height = BinaryPrimitives.ReadInt32BigEndian(result.Png.AsSpan(20, 4));
            }

            if (outputSize is not null && width > 0 && height > 0)
            {
                var dimensions = outputSize.Resolve(width, height);
                width = dimensions.Width;
                height = dimensions.Height;
            }

            return new(DateTime.Now, snapshot?.Target?.Title, snapshot?.Target?.Process, width, height);
        }

        private async Task AutoSaveScreenshotAsync(byte[] png, CapturedFrame? hdr,
            ScreenshotAutoSaveTriggers trigger, CancellationToken token, double captureScaling = 1)
        {
            var preferences = Settings.ScreenshotSave.Normalize();
            if ((preferences.AutoSaveTriggers & trigger) == 0) return;
            var result = new ScreenshotResult(png, ScreenshotOutputAction.Save, hdr)
            {
                ColorMode = Settings.CaptureColorMode == OutputColorMode.Both &&
                            hdr is not { ColorInfo.IsHdrSource: true }
                    ? OutputColorMode.Sdr
                    : Settings.CaptureColorMode,
                CaptureScaling = captureScaling,
            };
            await AutoSaveScreenshotAsync(result, trigger, token);
        }

        private async Task AutoSaveScreenshotAsync(ScreenshotResult result, ScreenshotAutoSaveTriggers trigger,
            CancellationToken token)
        {
            var preferences = Settings.ScreenshotSave.Normalize();
            if ((preferences.AutoSaveTriggers & trigger) == 0) return;
            result = await ScreenshotOutputDecoration.PrepareAsync(result, token);
            var profile = preferences.Automatic;
            var format = result.ColorMode == OutputColorMode.Sdr ? profile.Format : StaticImageFormat.Png;
            var path = Path.Combine(profile.ResolveDirectory(Settings.OutputDirectory),
                profile.CreateFileName(ScreenshotName(result, profile.OutputSize), format));
            await SaveScreenshotAsync(result, path, profile, false, true, token);
        }

        private async Task CopyScreenshotAsync(ScreenshotResult result, CancellationToken token)
        {
            var clipboard = _services.Context.Clipboard ??
                            throw new InvalidOperationException(UiText.Get("ClipboardFailed"));
            await ClipboardImage.CopyAsync(clipboard, result, Settings.ScreenshotClipboard,
                _services.Context.StorageProvider, Settings.ScreenshotSave.Quality, token);
        }
    }
}

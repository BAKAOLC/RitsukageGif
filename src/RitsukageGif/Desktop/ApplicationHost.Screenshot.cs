using System.Buffers.Binary;
using System.Diagnostics;
using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private readonly AsyncLocal<ScreenshotInvocation?> _screenshotInvocation = new();
        private SelectionResult? _lastScreenshotSelection;
        private CancellationTokenSource? _screenshotCancellation;
        private Task? _screenshotOperation;
        private SelectionCoordinator? _screenshotSelection;

        private bool CanTakeScreenshot => !IsClosing && !IsSelecting && !IsConfiguring && !IsTakingScreenshot;

        private bool IsTakingScreenshot =>
            _screenshotOperation is not null || _taskScreenshotRecordingCancellation is not null;

        private ScreenshotSavePreferences ScreenshotSaveForOutput => IsTaskHost
            ? Settings.ScreenshotSave with
            {
                AfterSave = ScreenshotAfterSaveAction.None,
                Notify = false,
                AutoSaveEnabled = false,
                AutoSaveTriggers = ScreenshotAutoSaveTriggers.None,
                Quick = Settings.ScreenshotSave.Quick with
                {
                    AfterSave = ScreenshotAfterSaveAction.None, Notify = false,
                },
            }
            : Settings.ScreenshotSave;

        private Task<ScreenshotInvocation> ScreenshotAsync()
        {
            return ScreenshotAsync(Settings.CaptureSource);
        }

        private Task<ScreenshotInvocation> ScreenshotAsync(CaptureSourceKind source, PixelRect? initialRegion = null,
            ScreenshotOutputAction? confirmAction = null, bool autoConfirm = false,
            WindowTarget? initialTarget = null,
            Func<Window?, ScreenshotResult, CancellationToken, Task<bool>>? outputHandler = null,
            bool wholeInitialTarget = false, TimeSpan delay = default, bool startWithScrolling = false,
            bool copyTextOutput = false, bool translateOutput = false, ActionScreenshotGeometry? initialGeometry = null,
            bool resolveActiveWindow = false, bool startWithRecording = false,
            ScreenshotInvocation? invocation = null)
        {
            invocation ??= new(false);
            if (!CanTakeScreenshot)
            {
                if (invocation.PropagateErrors) throw new InvalidOperationException(UiText.Get("CommandNotReady"));
                return Task.FromResult(invocation);
            }

            if (!Enum.IsDefined(source)) throw new ArgumentOutOfRangeException(nameof(source));
            var completion =
                new TaskCompletionSource<ScreenshotInvocation>(TaskCreationOptions.RunContinuationsAsynchronously);
            _screenshotOperation = completion.Task;
            _ = CompleteScreenshotAsync(completion, RunScreenshotAsync(source, initialRegion, confirmAction,
                    autoConfirm, initialTarget, outputHandler, wholeInitialTarget, delay, startWithScrolling,
                    copyTextOutput, translateOutput, initialGeometry, resolveActiveWindow, startWithRecording,
                    invocation),
                invocation);
            return completion.Task;
        }

        private CommandReply ScreenshotReply(ScreenshotInvocation invocation)
        {
            return new(true, CurrentState,
                Output: invocation.Paths.Count > 0 ? invocation.Paths[0] : null, Outputs: invocation.Paths,
                Region: invocation.Region);
        }

        private async Task CompleteScreenshotAsync(TaskCompletionSource<ScreenshotInvocation> completion,
            Task<ScreenshotCompletion?> operation, ScreenshotInvocation invocation)
        {
            try
            {
                Task? recording = null;
                using (var completed = await operation)
                {
                    if (IsClosing && IsTaskHost && !TaskKeepAlive && completed?.RecordingSelection is not null)
                        throw new OperationCanceledException(UiText.Get("Cancelled"), new CancellationToken(true));
                    if (!IsClosing && completed is not null)
                    {
                        if (completed.RecordingSelection is { } recordingSelection)
                        {
                            if (ReferenceEquals(_screenshotOperation, completion.Task)) _screenshotOperation = null;
                            recording = OpenScreenshotRecorderAsync(recordingSelection, invocation);
                        }
                        else if (completed.Translation is { } translation)
                        {
                            if (completed.TranslationMode == TranslationMode.ImageToImage)
                                await PinScreenshotTranslationAsync(translation, invocation);
                            else
                                _ = OpenScreenshotTranslationAsync(translation.Png);
                        }
                    }
                }

                if (recording is not null) await recording;
                completion.TrySetResult(invocation);
            }
            catch (Exception error)
            {
                completion.TrySetException(error);
            }
            finally
            {
                if (ReferenceEquals(_screenshotOperation, completion.Task)) _screenshotOperation = null;
                Notify();
            }
        }

        private async Task<ScreenshotCompletion?> RunScreenshotAsync(CaptureSourceKind source, PixelRect? initialRegion,
            ScreenshotOutputAction? confirmAction, bool autoConfirm, WindowTarget? initialTarget,
            Func<Window?, ScreenshotResult, CancellationToken, Task<bool>>? outputHandler,
            bool wholeInitialTarget, TimeSpan delay, bool startWithScrolling, bool copyTextOutput, bool translateOutput,
            ActionScreenshotGeometry? initialGeometry, bool resolveActiveWindow, bool startWithRecording,
            ScreenshotInvocation invocation)
        {
            var operationId = Interlocked.Increment(ref _nextOperationId);
            using var scope = HostLogger.BeginScope("ScreenshotOperation {ScreenshotOperationId}", operationId);
            var started = Stopwatch.GetTimestamp();
            HostLogger.ZLogInformation(
                $"Screenshot operation started. OperationId={operationId} Source={source.ToString()} InitialRegion={initialRegion} DelayMilliseconds={delay.TotalMilliseconds} AutoConfirm={autoConfirm} StartWithScrolling={startWithScrolling} StartWithRecording={startWithRecording}");
            using var cancellation = new CancellationTokenSource();
            _screenshotCancellation = cancellation;
            IsSelecting = true;
            RefreshDesktopToolbar();
            ScreenshotCompletion? completed = null;
            try
            {
                if (resolveActiveWindow)
                {
                    Notify();
                    initialTarget = await WindowLocator.ActiveWindowAsync(cancellation.Token)
                                    ?? throw new InvalidOperationException(UiText.Get("WindowNotFound"));
                    initialRegion = source == CaptureSourceKind.Window ? initialTarget.Client : initialTarget.Bounds;
                }

                var operation = CaptureScreenshotAsync(source, cancellation.Token, initialRegion, confirmAction,
                    autoConfirm, initialTarget, outputHandler, wholeInitialTarget, delay, startWithScrolling,
                    copyTextOutput, translateOutput, initialGeometry, startWithRecording, invocation);
                Notify();
                completed = await operation;
            }
            catch (OperationCanceledException error) when (cancellation.IsCancellationRequested ||
                                                           error.CancellationToken.IsCancellationRequested)
            {
                HostLogger.ZLogDebug($"Screenshot operation cancelled. OperationId={operationId} Closing={IsClosing}");
                if (invocation.PropagateErrors)
                    throw new OperationCanceledException("Screenshot cancelled.", new CancellationToken(true));
            }
            catch (Exception error) when (!IsClosing)
            {
                HostLogger.ZLogError(error, $"Screenshot operation failed. OperationId={operationId}");
                SetStatus(error.Message);
                if (invocation.PropagateErrors) throw;
            }
            finally
            {
                _screenshotSelection = null;
                _screenshotCancellation = null;
                IsSelecting = false;
                Notify();
                HostLogger.ZLogInformation(
                    $"Screenshot operation ended. OperationId={operationId} ElapsedMilliseconds={Stopwatch.GetElapsedTime(started).TotalMilliseconds} Closing={IsClosing}");
            }

            return completed;
        }

        private void CancelScreenshot()
        {
            HostLogger.ZLogDebug(
                $"Screenshot cancellation requested. HasSelection={_screenshotSelection is not null} HasCancellationSource={_screenshotCancellation is not null}");
            try
            {
                _screenshotCancellation?.Cancel();
                _taskScreenshotRecordingCancellation?.Cancel();
            }
            finally
            {
                _screenshotSelection?.Cancel();
            }
        }

        private async Task<ScreenshotCompletion?> CaptureScreenshotAsync(CaptureSourceKind source,
            CancellationToken token,
            PixelRect? initialRegion = null, ScreenshotOutputAction? confirmAction = null,
            bool autoConfirm = false, WindowTarget? initialTarget = null,
            Func<Window?, ScreenshotResult, CancellationToken, Task<bool>>? outputHandler = null,
            bool wholeInitialTarget = false, TimeSpan delay = default, bool startWithScrolling = false,
            bool copyTextOutput = false, bool translateOutput = false, ActionScreenshotGeometry? initialGeometry = null,
            bool startWithRecording = false, ScreenshotInvocation? invocation = null)
        {
            await WaitForCaptureHistoryAsync(token);
            var recordingRequested = false;
            NativeCaptureSessionReference? recordingSource = null;
            SelectionResult? recordingSelection = null;
            var selectionAccepted = false;
            var showPendingEditor = false;
            ScreenshotResult? translation = null;
            using var pasteTarget = !IsTaskHost && Settings.ScreenshotClipboard.PasteAfterCopy && !copyTextOutput &&
                                    !translateOutput &&
                                    outputHandler is null
                ? await ScreenshotPasteGuard.CaptureTargetAsync(token)
                : null;
            try
            {
                if (delay > TimeSpan.Zero) await Task.Delay(delay, token);
                token.ThrowIfCancellationRequested();
                PlayScreenshotSound(ScreenshotSoundTiming.Start);
                var action = confirmAction ?? ScreenshotOutputAction.Copy;
                var allowRecording = CanEditRecordingSettings && !IsBusy;
                var propagateOutputErrors = invocation?.PropagateErrors == true;
                var deferCopyOutput = invocation?.DeferCopyOutput == true;
                using var selection = new SelectionCoordinator(Screens, Settings.SnapRadius,
                    Settings.EdgeThreshold, source == CaptureSourceKind.Window, Settings.Cursor,
                    retainScreenshot: true, retainHdr: true,
                    screenshotToneMap: Settings.CaptureToneMap,
                    screenshotColorMode: Settings.CaptureColorMode)
                {
                    InitialRegion = initialRegion, InitialGeometry = initialGeometry, ConfirmAction = action,
                    AutoConfirm = autoConfirm, InitialTarget = initialTarget, WholeInitialTarget = wholeInitialTarget,
                    StartWithScrolling = startWithScrolling, StartWithRecording = startWithRecording,
                    AllowRecording = allowRecording, PropagateOutputErrors = propagateOutputErrors,
                    DeferCopyOutput = deferCopyOutput,
                };
                _screenshotSelection = selection;
                HostLogger.ZLogDebug($"Screenshot selection coordinator created. Source={source.ToString()}");
                ConfigureSelectionPreferences(selection);
                if (!IsTaskHost && Settings.ScreenshotSave.AutoSaveEnabled &&
                    (Settings.ScreenshotSave.AutoSaveTriggers & ScreenshotAutoSaveTriggers.Scrolling) != 0)
                    selection.ScrollingCloseOutputHandler = async (result, captureToken) =>
                    {
                        try
                        {
                            await AutoSaveScreenshotAsync(result, ScreenshotAutoSaveTriggers.Scrolling, captureToken);
                        }
                        catch (Exception error) when (error is not OperationCanceledException ||
                                                      !captureToken.IsCancellationRequested)
                        {
                            SetStatus(error.Message);
                            throw;
                        }
                    };
                selection.RecognitionCopied = () => PlayScreenshotSound(ScreenshotSoundTiming.Copy);
                selection.WindowPinOutputHandler = (target, region, cursor, scaling, captureToken) =>
                    _pinnedImages.PinWindowRoiAsync(target, region, cursor, scaling, captureToken);
                // ReSharper disable AccessToDisposedClosure
                selection.RecognitionOutputHandler = async (result, trigger, cancellationToken) =>
                {
                    var previousInvocation = _screenshotInvocation.Value;
                    _screenshotInvocation.Value = invocation;
                    try
                    {
                        _captureHistory.RecordScreenshot(result.Png, result.OutputRegion ?? selection.Snapshot.Region,
                            selection.WindowSource ? CaptureSourceKind.Window : CaptureSourceKind.Screen, result.Hdr,
                            result.CaptureScaling, result.Decoration, result.OutputOrigin);
                        if (selection.WasScrollingScreenshot) trigger = ScreenshotAutoSaveTriggers.Scrolling;
                        try
                        {
                            await AutoSaveScreenshotAsync(result, trigger, cancellationToken);
                        }
                        catch (Exception error) when (error is not OperationCanceledException ||
                                                      !cancellationToken.IsCancellationRequested)
                        {
                            HostLogger.ZLogError(error, $"Screenshot recognition automatic save failed.");
                            SetStatus(error.Message);
                            if (invocation?.PropagateErrors == true) throw;
                        }
                    }
                    catch (Exception error) when (invocation?.PropagateErrors == true)
                    {
                        selection.FailOutput(error);
                        throw;
                    }
                    finally
                    {
                        _screenshotInvocation.Value = previousInvocation;
                    }
                };

                selection.ScreenshotOutputHandler = OutputAsync;
                selection.ScrollingFileOutputHandler =
                    (owner, scrollAction, mode, scaling, decoration, annotations, cancellation) =>
                        SaveStreamingScreenshotAsync(owner, scrollAction, mode, scaling, decoration, annotations,
                            invocation,
                            cancellation);
                // ReSharper restore AccessToDisposedClosure
                HostLogger.ZLogInformation($"Screenshot selection initialization requested.");
                if (await selection.ShowAsync() is { } completed)
                {
                    token.ThrowIfCancellationRequested();
                    if (selection.DeferredOutput is { } deferredOutput &&
                        !await OutputAsync(null, deferredOutput, token))
                        return null;
                    if (selection.DeferredRecognition is { } deferredRecognition)
                        await RunScreenshotRecognitionAsync(deferredRecognition, selection.RecognitionOutputHandler,
                            propagateOutputErrors, token);
                    token.ThrowIfCancellationRequested();
                    selectionAccepted = true;
                    invocation?.Region = completed.Region;
                    _lastScreenshotSelection = completed with { CaptureSession = null };
                    if (selection.RecordingRequested)
                    {
                        token.ThrowIfCancellationRequested();
                        recordingSource = selection.CommitSourceReference(token);
                        recordingSelection = completed with { CaptureSession = recordingSource?.Session };
                        recordingRequested = true;
                    }

                    HostLogger.ZLogInformation($"Screenshot selection accepted. Region={completed.Region}");
                }
                else
                {
                    HostLogger.ZLogDebug($"Screenshot selection returned without an accepted result.");
                    if (invocation?.PropagateErrors == true)
                    {
                        if (selection.CompletionNotice is { } failure) throw new InvalidOperationException(failure);
                        throw new OperationCanceledException("Screenshot cancelled.", new CancellationToken(true));
                    }
                }

                if (selection.CompletionNotice is { } notice && !token.IsCancellationRequested) SetStatus(notice);
                if (selectionAccepted && selection.TranslationRequested is { } requestedTranslation)
                    translation = requestedTranslation;
                RememberAnnotationPreferences(selection);
                RememberFrameDecorationPreferences(selection);
                token.ThrowIfCancellationRequested();
                if (pasteTarget is not null && !IsClosing)
                    await pasteTarget.TryPasteAsync(token);
                showPendingEditor = selectionAccepted;
                if (!selectionAccepted || (!recordingRequested && translation is null)) return null;
                var completion = new ScreenshotCompletion(recordingSelection, translation,
                    selection.TranslationPreferences.Mode,
                    recordingSource);
                recordingSource = null;
                return completion;

                // ReSharper disable AccessToDisposedClosure
                async Task<bool> OutputAsync(Window? owner, ScreenshotResult result,
                    CancellationToken cancellationToken)
                {
                    var previousInvocation = _screenshotInvocation.Value;
                    _screenshotInvocation.Value = invocation;
                    try
                    {
                        var raw = result with
                        {
                            CaptureScaling = selection.ScreenshotCaptureScaling,
                            IsScrollingCapture = selection.WasScrollingScreenshot,
                        };
                        result = copyTextOutput
                            ? raw
                            : await ScreenshotOutputDecoration.PrepareAsync(raw, cancellationToken);
                        bool success;
                        if (translateOutput && result.Action == ScreenshotOutputAction.Copy)
                        {
                            translation = result;
                            success = true;
                        }
                        else
                        {
                            success = await (outputHandler ?? OutputScreenshotAsync)(owner, result, cancellationToken);
                        }

                        if (!success)
                        {
                            if (invocation?.PropagateErrors == true)
                                throw new OperationCanceledException("Screenshot cancelled.",
                                    new CancellationToken(true));
                            return false;
                        }

                        if (pasteTarget is not null && result.Action == ScreenshotOutputAction.Copy)
                            await pasteTarget.MarkClipboardCopiedAsync(cancellationToken);
                        _captureHistory.RecordScreenshot(raw.Png, raw.OutputRegion ?? selection.Snapshot.Region,
                            selection.WindowSource ? CaptureSourceKind.Window : CaptureSourceKind.Screen, raw.Hdr,
                            raw.CaptureScaling, raw.Decoration, raw.OutputOrigin);
                        var trigger = result.Action switch
                        {
                            ScreenshotOutputAction.Copy => ScreenshotAutoSaveTriggers.Copy,
                            ScreenshotOutputAction.Pin => ScreenshotAutoSaveTriggers.Pin,
                            ScreenshotOutputAction.Save or ScreenshotOutputAction.QuickSave
                                or ScreenshotOutputAction.ExportPdf => ScreenshotAutoSaveTriggers
                                    .Save,
                            ScreenshotOutputAction.Edit => ScreenshotAutoSaveTriggers.Screenshot,
                            _ => ScreenshotAutoSaveTriggers.None,
                        };
                        if ((copyTextOutput || translateOutput) && result.Action == ScreenshotOutputAction.Copy)
                            trigger = translateOutput
                                ? ScreenshotAutoSaveTriggers.Translate |
                                  (selection.TranslationPreferences.Mode == TranslationMode.ImageToImage
                                      ? ScreenshotAutoSaveTriggers.Pin
                                      : ScreenshotAutoSaveTriggers.None)
                                : ScreenshotAutoSaveTriggers.Recognize;
                        if (selection.WasScrollingScreenshot) trigger = ScreenshotAutoSaveTriggers.Scrolling;
                        try
                        {
                            await AutoSaveScreenshotAsync(result, trigger, cancellationToken);
                        }
                        catch (Exception error)
                        {
                            SetStatus(error.Message);
                            if (invocation?.PropagateErrors == true) throw;
                        }

                        return true;
                    }
                    catch (Exception error) when (invocation?.PropagateErrors == true)
                    {
                        selection.FailOutput(error);
                        throw;
                    }
                    finally
                    {
                        _screenshotInvocation.Value = previousInvocation;
                    }
                }
                // ReSharper restore AccessToDisposedClosure
            }
            finally
            {
                recordingSource?.Dispose();
                if (showPendingEditor && !token.IsCancellationRequested &&
                    invocation?.CommandToken.IsCancellationRequested != true)
                    ShowPendingImageEditor();
                else
                    _pendingImageEdit = null;
            }
        }

        private async Task<bool> OutputScreenshotAsync(Window? owner, ScreenshotResult result,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            switch (result.Action)
            {
                case ScreenshotOutputAction.Edit:
                    return await EditScreenshotAsync(result, token);
                case ScreenshotOutputAction.ExportPdf:
                    return await ExportScreenshotDocumentAsync(owner, result, token);
                case ScreenshotOutputAction.Print:
                    return await PrintScreenshotDocumentAsync(owner, result, token);
                case ScreenshotOutputAction.Copy or ScreenshotOutputAction.Save or
                    ScreenshotOutputAction.Pin or ScreenshotOutputAction.QuickSave:
                    break;
            }

            var mode = result.ColorMode;
            if (mode != OutputColorMode.Sdr && result.Hdr is not { ColorInfo.IsHdrSource: true })
                throw new NotSupportedException(result.HdrUnavailableReason ?? UiText.Get("HdrCaptureUnavailable"));
            var context = owner ?? _services.Context;
            // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
            switch (result.Action)
            {
                case ScreenshotOutputAction.Pin:
                    await PinScreenshotAsync(result, token);
                    return true;
                case ScreenshotOutputAction.Copy:
                    await CopyScreenshotAsync(result, token);
                    PlayScreenshotSound(ScreenshotSoundTiming.Copy);
                    return true;
                case ScreenshotOutputAction.QuickSave:
                {
                    var quickProfile = ScaleScreenshotSave(result, ScreenshotSaveForOutput.Normalize().Quick);
                    var format = mode == OutputColorMode.Sdr ? quickProfile.Format : StaticImageFormat.Png;
                    var quickPath = quickProfile.CreatePath(Settings.OutputDirectory,
                        ScreenshotName(result, quickProfile.OutputSize), format);
                    var saved = await SaveScreenshotAsync(result, quickPath, quickProfile, false, false, token,
                        owner as ScreenshotScrollingWindow);
                    if (saved) PlayScreenshotSound(ScreenshotSoundTiming.QuickSave);
                    return saved;
                }
                case ScreenshotOutputAction.Save:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(result));
            }

            var profile = ScaleScreenshotSave(result, ScreenshotSaveForOutput.ManualProfile());
            var path = await ScreenshotOutputStorage.PickPathAsync(owner, context.StorageProvider,
                profile, Settings.OutputDirectory, "Screenshot", mode, Settings.Appearance.SaveDialogStyle,
                ScreenshotName(result, profile.OutputSize), token);
            var completed = path is not null && await SaveScreenshotAsync(result, path, profile, true, false, token,
                owner as ScreenshotScrollingWindow);
            if (completed) PlayScreenshotSound(ScreenshotSoundTiming.Save);
            return completed;
        }

        private async Task<bool> SaveScreenshotAsync(ScreenshotResult result, string path,
            ScreenshotSaveProfile profile, bool rememberExtension, bool automatic, CancellationToken token,
            ScreenshotScrollingWindow? scrolling = null)
        {
            var output = scrolling is { IsFileOnlyCapture: true }
                ? await ScreenshotScrollingSaveProgressWindow.RunAsync(scrolling,
                    saveToken => ScreenshotImageStorage.SaveAsync(result, path, profile, saveToken), token)
                : await ScreenshotImageStorage.SaveAsync(result, path, profile, token);
            if (output is null) return false;
            LastScreenshotOutput = output;
            _screenshotInvocation.Value?.AddPaths(output.Paths);
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
                var completionToken = _screenshotInvocation.Value?.PropagateErrors == true
                    ? token
                    : CancellationToken.None;
                await ScreenshotSaveCompletion.RunAsync(output.Paths, profile, _services.Context.Clipboard,
                    _services.Context.StorageProvider, () => CopyScreenshotAsync(result, completionToken),
                    ShowOutputNotification, completionToken);
            }
            catch (Exception error)
            {
                SetStatus(error.Message);
                if (_screenshotInvocation.Value?.PropagateErrors == true) throw;
            }
            finally
            {
                if (automatic)
                    QueueAutomaticScreenshotCleanup(output.Paths);
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
            if (IsTaskHost) return;
            var preferences = Settings.ScreenshotSave.Normalize();
            if (!preferences.AutoSaveEnabled || (preferences.AutoSaveTriggers & trigger) == 0) return;
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
            if (IsTaskHost) return;
            var preferences = Settings.ScreenshotSave.Normalize();
            if (!preferences.AutoSaveEnabled || (preferences.AutoSaveTriggers & trigger) == 0) return;
            result = await ScreenshotOutputDecoration.PrepareAsync(result, token);
            var profile = ScaleScreenshotSave(result, preferences.Automatic);
            var format = result.ColorMode == OutputColorMode.Sdr ? profile.Format : StaticImageFormat.Png;
            var path = profile.CreatePath(Settings.OutputDirectory, ScreenshotName(result, profile.OutputSize), format);
            await SaveScreenshotAsync(result, path, profile, false, true, token);
        }

        private async Task<PinnedImageWindow?> PinScreenshotAsync(ScreenshotResult result, CancellationToken token)
        {
            var pin = result.ColorMode == OutputColorMode.Sdr
                ? await _pinnedImages.PinAsync(result.Png, result.CaptureScaling, result.OutputOrigin,
                    result.ContentOffset, result.WindowShape, token)
                : await _pinnedImages.PinHdrAsync(result.Png, result.Hdr!, result.CaptureScaling, result.OutputOrigin,
                    result.ContentOffset, result.WindowShape, token);
            if (!IsTaskHost && Settings.CapturePreferences.PinAndCopy) await CopyScreenshotAsync(result, token);
            PlayScreenshotSound(ScreenshotSoundTiming.Pin);
            return pin;
        }

        private async Task CopyScreenshotAsync(ScreenshotResult result, CancellationToken token)
        {
            var clipboard = _services.Context.Clipboard ??
                            throw new InvalidOperationException(UiText.Get("ClipboardFailed"));
            var (image, preferences) = ScaleScreenshotCopy(result);
            var output = ScreenshotOutputTransform.ResolveClipboardSize(image, preferences);
            await ClipboardImage.CopyAsync(clipboard, image, preferences,
                _services.Context.StorageProvider, Settings.ScreenshotSave.Quality,
                new(Settings.ScreenshotSave, ScreenshotName(image, output.OutputSize)), token);
        }
    }
}

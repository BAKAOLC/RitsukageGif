using System.ComponentModel;
using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotRecordingController : IDisposable
    {
        private static readonly ILogger Logger = LogHub.GetLogger("Desktop.ScreenshotRecording");
        private readonly ApplicationHost _host;
        private readonly CancellationTokenSource _lifetime = new();
        private readonly ScreenshotRecordingAudioDiscovery _audioDiscovery;

        private readonly string _workspaceRoot =
            Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
        private Task _completion = Task.CompletedTask;
        private Task<RecordingOutcome>? _operation;
        private ScreenshotRecordingPreferences _preferences;

        private IReadOnlyList<RecordingCameraDevice> _cameraDevices = [];
        private Task? _closeTask;
        private bool _discard;
        private bool _transition;
        private string? _workspace;

        internal SelectionResult Selection { get; private set; }

        internal ScreenshotRecordingController(ApplicationHost host, SelectionResult selection)
        {
            _host = host;
            _audioDiscovery = new(EnumerateAudioDevices, _lifetime.Token);
            _preferences = host.Settings.ScreenshotRecording.Normalize();
            ApplyPreferences();
            Camera = ScreenshotRecordingPreferenceAdapter.RestoreCamera(_preferences.Camera, []);
            Selection = selection;
            SelectSource(selection);
            Cursor = selection.Cursor;
            _host.Changed += HostChanged;
        }
        internal int Fps { get; set; } = 24;
        internal int DelaySeconds { get; set; }
        internal const int Divisor = 1;
        private bool Cursor { get; set; }
        internal bool Quick { get; set; }
        internal bool SystemAudio { get; set; } = true;
        internal bool Microphone { get; set; } = true;

        internal bool RecordKeyboardActions
        {
            get;
            private set
            {
                if (field == value) return;
                var recording = IsRecording;
                var enabled = recording && InputTracks is { } tracks ? tracks.SetKeyboard(value) : value;
                if (field == enabled) return;
                field = enabled;
                Logger.ZLogInformation(
                    $"Recording input track changed. Track=Keyboard Enabled={enabled} Recording={recording}");
            }
        } = true;

        internal bool RecordMouseActions
        {
            get;
            private set
            {
                if (field == value) return;
                var recording = IsRecording;
                var enabled = recording && InputTracks is { } tracks ? tracks.SetMouse(value) : value;
                if (field == enabled) return;
                field = enabled;
                Logger.ZLogInformation(
                    $"Recording input track changed. Track=Mouse Enabled={enabled} Recording={recording}");
            }
        } = true;

        internal RecordingInputTrackState? InputTracks { get; private set; }

        internal string? InputUnavailableReason => RecordingInputObserver.UnavailableReason ??
                                                   (IsRecording ? InputTracks?.UnavailableReason : null);

        internal bool CanChangeKeyboardActions => !Quick && InputUnavailableReason is null &&
                                                  (CanConfigure ||
                                                   (IsRecording && InputTracks?.KeyboardAvailable == true));

        internal bool CanChangeMouseActions => !Quick && InputUnavailableReason is null &&
                                               (CanConfigure || (IsRecording && InputTracks?.MouseAvailable == true));

        internal RecordingCameraOptions Camera { get; private set; }
        internal bool IsClosed { get; private set; }
        internal bool Reviewing { get; private set; }
        internal bool IsRecording => _operation is { IsCompleted: false } && _host.IsRecording;
        internal bool IsPaused => IsRecording && _host.IsRecordingPaused;
        internal bool CanPause => IsRecording && _host.CanPauseRecording;
        internal bool CanConfigure => !IsClosed && !Reviewing && !_transition && !IsPreparingSource && !_host.IsBusy;
        internal bool IsWorking => _transition || IsPreparingSource || _operation is { IsCompleted: false };
        internal string? Error { get; private set; }
        internal RecordingProgress? Progress => _operation is null ? null : _host.LatestProgress;
        internal CancellationToken Lifetime => _lifetime.Token;
        internal Func<CancellationToken, Task>? PrepareCapture { get; set; }
        internal Func<bool, CancellationToken, Task>? CompleteCapturePreparation { get; set; }
        internal Action<IReadOnlyList<string>, PixelRect>? PublishOutput { private get; set; }

        public void Dispose()
        {
            _host.Changed -= HostChanged;
            _lifetime.Cancel();
            _lifetime.Dispose();
            _cameraGate.Dispose();
            ReleaseSelectedSource();
            ClearWorkspace();
        }

        internal Task<ScreenshotRecordingAudioDiscovery.Snapshot> ReadAudioReadinessAsync(bool refresh = false)
        {
            return _audioDiscovery.ReadAsync(refresh);
        }

        private static IReadOnlyList<AudioCaptureDevice> EnumerateAudioDevices(bool system, CancellationToken token)
        {
            const int enoent = 2;
            try
            {
                return AudioCaptureBackend.EnumerateDevices(system, token);
            }
            catch (Win32Exception error) when (OperatingSystem.IsLinux() && error.NativeErrorCode == enoent)
            {
                return [];
            }
        }

        internal event Action? Changed;
        internal event Action? Completed;

        private void ConfigureCursor(bool visible)
        {
            Cursor = visible;
            _host.SetCaptureCursor(visible);
            Notify();
        }

        internal void ConfigurationChanged()
        {
            _preferences = _host.Settings.ScreenshotRecording with
            {
                Fps = Fps, DelaySeconds = DelaySeconds, Quick = Quick, SystemAudio = SystemAudio,
                Microphone = Microphone,
                RecordKeyboardActions = RecordKeyboardActions, RecordMouseActions = RecordMouseActions,
            };
            _host.UpdateScreenshotRecordingPreferences(_preferences);
            Notify();
        }

        internal void UpdateCameraPreviewOptions(RecordingCameraOptions options)
        {
            _cameraFailure = null;
            if (Camera == options) return;
            Camera = options;
            Notify();
        }

        internal void UpdateSelection(SelectionResult selection)
        {
            if (!CanConfigure) return;
            SelectSource(selection);
            Cursor = selection.Cursor;
            Error = null;
            _operation = null;
            _ = PrepareSelectedSourceAsync();
            Notify();
        }

        internal void UpdateRegion(PixelRect region)
        {
            if (!CanConfigure || region.IsEmpty) return;
            if (Selection is { WindowSource: true, Target: { } target }) region = region.Intersect(target.Client);
            if (region.IsEmpty) return;
            var next = Selection with { Region = region, Shape = null };
            Selection = next;
            Notify();
        }

        internal Task StartAsync(Window owner)
        {
            if (!CanConfigure) return Task.CompletedTask;
            _startTask = StartCoreAsync(owner);
            return _startTask;
        }

        private async Task StartCoreAsync(Window owner)
        {
            _transition = true;
            Error = null;
            Notify();
            var token = _lifetime.Token;
            var quick = Quick;
            var delay = DelaySeconds;
            var prepared = false;
            var started = false;
            try
            {
                string? path = null;
                if (quick)
                {
                    path = await PrepareQuickRecordingAsync(owner, token);
                    if (path is null) return;
                }
                else
                {
                    ClearWorkspace();
                    if (_workspace is not null)
                    {
                        Error = UiText.Get("RecordingWorkspaceCleanupFailed");
                        return;
                    }
                }

                var systemAudio = false;
                var microphone = false;
                if (AudioCaptureBackend.IsSupported && (SystemAudio || Microphone))
                {
                    var audio = await ReadAudioReadinessAsync(true);
                    (systemAudio, microphone) = audio.Select(SystemAudio, Microphone);
                }

                token.ThrowIfCancellationRequested();
                var video = quick || systemAudio || microphone;
                var colors = _host.Settings.CaptureColorMode;
                var cursor = Cursor;
                var capture = new RecordingOptions(Selection.Region, Fps, Divisor, Cursor,
                    OutputFormat.APng, GifQuality.Best);
                var reviewDocument = new RecordingReviewDocument([], video, Selection.Region,
                    _host.CreateRecordingReviewOutputContext())
                {
                    CaptureTitle = Selection.Target?.Title, CaptureProcess = Selection.Target?.Process,
                    PublishOutput = PublishOutput,
                    Colors = colors, FramesPerSecond = capture.Fps,
                    InputEffects = CaptureInputEffects(cursor),
                    ProgressEffects = _host.Settings.ScreenshotRecording.ProgressEffects,
                    InputEffectsChanged = ConfigureInputEffects, ProgressEffectsChanged = ConfigureProgressEffects,
                    CaptureCursorChanged = ConfigureCursor,
                    Format = _preferences.ReviewFormat, FormatChanged = ConfigureReviewFormat,
                    Quality = _preferences.ReviewQuality, QualityChanged = ConfigureReviewQuality,
                    AutoExitAfterEncoding = _preferences.AutoExitAfterEncoding,
                    AutoExitAfterEncodingChanged = ConfigureAutoExitAfterEncoding,
                };
                var control = new RecordingControl();
                if (PrepareCapture is { } prepare)
                {
                    prepared = true;
                    await prepare(token);
                }

                await CaptureWindowVisibility.FlushAsync(token);
                if (!await WaitForCountdownAsync(delay, token)) return;
                if (delay != 0) await CaptureWindowVisibility.FlushAsync(token);
                token.ThrowIfCancellationRequested();
                if (!quick)
                {
                    _workspace = Path.Combine(_workspaceRoot, "RitsukageGif-record-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(_workspace);
                    path = Path.Combine(_workspace, video ? "capture.mkv" : "capture.png");
                }

                if (path is null) return;
                InputTracks = new(!quick && RecordKeyboardActions, !quick && RecordMouseActions);
                _operation = video
                    ? _host.StartScreenshotVideoRecordingAsync(Selection, new(capture)
                        {
                            Container = VideoContainer.Mp4,
                            Quality = quick ? RecordingEncodingQuality.VideoCrf(_preferences.QuickQuality) : 0,
                            Profile = quick ? VideoRecordingProfile.Standard : VideoRecordingProfile.ReviewIntermediate,
                            ColorMode = colors, ToneMap = _host.Settings.CaptureToneMap,
                        }, path, systemAudio, null, microphone, null,
                        !quick,
                        !quick && RecordKeyboardActions,
                        !quick && RecordMouseActions, control)
                    : _host.StartScreenshotAnimationRecordingAsync(Selection, capture, _workspace!,
                        RecordKeyboardActions,
                        RecordMouseActions, control);
                Logger.ZLogInformation(
                    $"Screenshot recording started. Quick={quick} Video={video} Region={Selection.Region}");
                _completion = ObserveAsync(owner, _operation, quick, reviewDocument, token);
                started = true;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                Error = ApplicationHost.VideoErrorMessage(error);
                Logger.ZLogError(error, $"Screenshot recording could not start.");
                ClearWorkspace();
            }
            finally
            {
                if (prepared && CompleteCapturePreparation is { } complete)
                    try
                    {
                        await complete(!started && !token.IsCancellationRequested, token);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                    }
                    catch (Exception error)
                    {
                        Error ??= ApplicationHost.VideoErrorMessage(error);
                        Logger.ZLogError(error, $"Screenshot recording capture policy could not be restored.");
                    }

                _transition = false;
                Notify();
            }
        }

        internal async Task StopAsync()
        {
            if (_countdown is { } countdown)
            {
                await countdown.CancelAsync();
                await _startTask;
                return;
            }

            if (!IsRecording) return;
            Logger.ZLogInformation($"Screenshot recording stop requested.");
            await _host.StopOwnedVideoAsync(_operation, false);
        }

        internal void TogglePause()
        {
            if (!CanPause) return;
            _host.PauseOwnedRecording(_operation, !IsPaused);
            Logger.ZLogInformation($"Screenshot recording pause changed. Paused={IsPaused}");
        }

        internal async Task RerecordAsync()
        {
            if (IsClosed || Reviewing || _transition) return;
            _transition = true;
            _discard = true;
            Logger.ZLogInformation($"Screenshot recording restart requested.");
            Notify();
            try
            {
                await _host.StopOwnedVideoAsync(_operation, true);
                await _completion;
                _operation = null;
            }
            finally
            {
                _discard = false;
                _transition = false;
            }

            HostChanged();
            await PrepareSelectedSourceAsync();
            await EnsureCameraReadyAsync();
            Notify();
        }

        internal Task CloseAsync()
        {
            if (_closeTask is { } pending) return pending;
            if (IsClosed) return Task.CompletedTask;
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _closeTask = completion.Task;
            IsClosed = true;
            _ = CompleteCloseAsync(completion);
            return completion.Task;
        }

        private async Task CompleteCloseAsync(TaskCompletionSource completion)
        {
            try
            {
                await CloseCoreAsync();
                completion.SetResult();
            }
            catch (Exception error)
            {
                completion.SetException(error);
            }
        }

        private async Task CloseCoreAsync()
        {
            try
            {
                Logger.ZLogInformation(
                    $"Screenshot recording close requested. Recording={IsRecording} Reviewing={Reviewing}");
                try
                {
                    await _lifetime.CancelAsync();
                }
                catch (Exception error)
                {
                    Logger.ZLogError(error, $"Screenshot recording cancellation callback failed.");
                }

                Notify();
                await _startTask;
                await _sourcePreparation;
                await _host.StopOwnedVideoAsync(_operation, true);
                await _completion;
            }
            finally
            {
                try
                {
                    try
                    {
                        await StopCameraAsync();
                    }
                    finally
                    {
                        await ReleaseRecorderExclusionAsync();
                    }
                }
                finally
                {
                    ReleaseSelectedSource();
                    ClearWorkspace();
                    Completed?.Invoke();
                }
            }
        }

        private async Task ObserveAsync(Window owner, Task<RecordingOutcome> operation, bool quick,
            RecordingReviewDocument document, CancellationToken token)
        {
            var finished = false;
            try
            {
                var result = await operation;
                await StopCameraAsync();
                await ReleaseRecorderExclusionAsync();
                Logger.ZLogInformation(
                    $"Screenshot recording finished. Cancelled={result.Cancelled} Failed={result.Error is not null} Quick={quick}");
                if (IsClosed || _discard) return;
                if (result is { SourceLost: true, Error: null or CaptureSourceLostException } &&
                    (quick || result.Outputs.Count == 0))
                {
                    Error = UiText.Get("WindowSourceLostStopped");
                    _operation = null;
                    return;
                }

                if (result.Error is { } error) throw error;
                if (result.Cancelled) return;
                if (!quick)
                {
                    Reviewing = true;
                    Notify();
                    var review = await RecordingReviewWindow.ShowAsync(owner, document with
                    {
                        Paths = result.Outputs, SourceLost = result.SourceLost,
                        RecordedDuration = result.RecordedDuration, AcceptedCaptures = result.AcceptedCaptures,
                        InputEffects = CaptureInputEffects(document.InputEffects.ShowCursor),
                        ProgressEffects = _host.Settings.ScreenshotRecording.ProgressEffects,
                    }, token);
                    if (review == RecordingReviewResult.Rerecord)
                    {
                        _operation = null;
                        return;
                    }
                }
                else
                {
                    PublishOutput?.Invoke(result.Outputs, document.CaptureRegion);
                }

                finished = true;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                Error = ApplicationHost.VideoErrorMessage(error);
                Logger.ZLogError(error, $"Screenshot recording failed.");
            }
            finally
            {
                Reviewing = false;
                if (finished) IsClosed = true;
                ClearWorkspace();
                HostChanged();
                if (!IsClosed && !_discard)
                    try
                    {
                        await PrepareSelectedSourceAsync();
                        await EnsureCameraReadyAsync();
                    }
                    catch (Exception error)
                    {
                        Error = ApplicationHost.VideoErrorMessage(error);
                        Logger.ZLogError(error, $"Recording camera could not resume after recording.");
                    }

                Notify();
            }

            if (finished && _closeTask is null) Completed?.Invoke();
        }

        private void ConfigureReviewFormat(RecordingReviewFormat format)
        {
            _preferences = _preferences with { ReviewFormat = format };
            _host.UpdateScreenshotRecordingPreferences(
                _host.Settings.ScreenshotRecording with { ReviewFormat = format });
        }

        private void ConfigureReviewQuality(RecordingReviewQuality quality)
        {
            _preferences = _preferences with { ReviewQuality = quality };
            _host.UpdateScreenshotRecordingPreferences(_host.Settings.ScreenshotRecording with
            {
                ReviewQuality = quality,
            });
        }

        private void ConfigureAutoExitAfterEncoding(bool enabled)
        {
            _preferences = _preferences with { AutoExitAfterEncoding = enabled };
            _host.UpdateScreenshotRecordingPreferences(_host.Settings.ScreenshotRecording with
            {
                AutoExitAfterEncoding = enabled,
            });
        }

        private void ClearWorkspace()
        {
            var directory = _workspace;
            if (directory is null) return;
            directory = Path.GetFullPath(directory);
            if (!string.Equals(Path.GetDirectoryName(directory), _workspaceRoot, StringComparison.Ordinal) ||
                !Path.GetFileName(directory).StartsWith("RitsukageGif-record-", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "The recording workspace is outside its owned temporary directory.");
            try
            {
                Directory.Delete(directory, true);
                _workspace = null;
            }
            catch (DirectoryNotFoundException)
            {
                _workspace = null;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Logger.ZLogWarning(error,
                    $"Screenshot recording workspace could not be removed. Directory={directory}");
            }
        }

        private void Notify()
        {
            _host.UpdateScreenshotRecordingPreparation(!IsClosed && _transition, !IsClosed && IsCountingDown);
            Changed?.Invoke();
        }

        private void ApplyPreferences()
        {
            Fps = _preferences.Fps;
            DelaySeconds = _preferences.DelaySeconds;
            Quick = _preferences.Quick;
            SystemAudio = _preferences.SystemAudio;
            Microphone = _preferences.Microphone;
            RecordKeyboardActions = _preferences.RecordKeyboardActions;
            RecordMouseActions = _preferences.RecordMouseActions;
        }

        private void HostChanged()
        {
            if (CanConfigure && _host.Settings.ScreenshotRecording != _preferences)
            {
                var cameraChanged = _host.Settings.ScreenshotRecording.Camera != _preferences.Camera;
                _preferences = _host.Settings.ScreenshotRecording.Normalize();
                ApplyPreferences();
                if (cameraChanged)
                {
                    Camera = ScreenshotRecordingPreferenceAdapter.RestoreCamera(_preferences.Camera, _cameraDevices);
                    _ = RefreshCameraAsync();
                }
            }

            Notify();
        }
    }
}

using System.Runtime.InteropServices;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private VideoRecordingWindow? _videoRecorder;

        // ReSharper disable once MemberCanBePrivate.Global
        public Task OpenVideoRecorderAsync()
        {
            if (IsClosing) return Task.CompletedTask;
            if (_videoRecorder is null)
            {
                _videoRecorder = new(this);
                _videoRecorder.Closed += (_, _) => _videoRecorder = null;
            }

            WindowActivation.Show(_videoRecorder);
            return Task.CompletedTask;
        }

        private void CloseVideoRecorderForShutdown()
        {
            _videoRecorder?.CloseForShutdown();
        }

        internal Task<RecordingOutcome> StartVideoRecordingAsync(VideoRecordingOptions options, string path,
            bool systemAudio, string? systemDevice, bool microphone, string? microphoneDevice,
            bool retainOutput = false)
        {
            Dispatcher.UIThread.VerifyAccess();
            if (IsClosing || IsBusy || IsSelecting || IsConfiguring || Region is null)
                throw new InvalidOperationException(UiText.Get("CommandNotReady"));
            var preset = _presets.ActivePreset ?? _manualPreset;
            if (preset?.Layout is { } layout)
                options = options with
                {
                    Capture = options.Capture with { Region = new(0, 0, layout.Width, layout.Height) },
                };
            options.Validate();
            if (FfmpegEncoder.FindExecutable() is null)
                throw new FfmpegDependencyException(UiText.Get("FfmpegMissing"));
            if ((systemAudio || microphone) && !OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException(UiText.Get("VideoAudioUnavailable"));
            _presets.PrepareVideoRecording();
            View?.PausePreview();
            LatestProgress = null;
            _manualCaptureCompleted = false;
            _recordSettings = Settings with
            {
                Fps = options.Capture.Fps, Divisor = options.Capture.Divisor, Cursor = options.Capture.Cursor,
            };
            _streams = [];
            _stop = new();
            _abort = new();
            IsBusy = IsRecording = true;
            _recordClock.Restart();
            Notify();
            _operation = RecordVideoAsync(options, path, preset, systemAudio, systemDevice, microphone,
                microphoneDevice, retainOutput, _stop.Token, _abort.Token);
            return _operation;
        }

        internal async Task StopOwnedVideoAsync(Task<RecordingOutcome>? operation, bool cancel)
        {
            if (operation is null || operation.IsCompleted || !ReferenceEquals(operation, _operation)) return;
            if (cancel) CancelRecording();
            else if (IsRecording) await ToggleRecordingAsync();
            await operation;
        }

        internal static string VideoErrorMessage(Exception error)
        {
            if (error is not COMException) return error.Message;
            var key = unchecked((uint)error.HResult) switch
            {
                0x8889000A => "VideoAudioDeviceBusy",
                0x88890004 => "VideoAudioDeviceLost",
                0x80070005 => "VideoAudioPermissionDenied",
                0x80070490 => "VideoAudioDeviceMissing",
                _ => null,
            };
            return key is null ? error.Message : UiText.Get(key);
        }

        private async Task<RecordingOutcome> RecordVideoAsync(VideoRecordingOptions options, string path,
            WindowPreset? preset, bool systemAudio, string? systemDevice, bool microphone, string? microphoneDevice,
            bool retainOutput, CancellationToken stop, CancellationToken cancel)
        {
            var operationId = Interlocked.Increment(ref _nextOperationId);
            using var scope = HostLogger.BeginScope("VideoOperation {VideoOperationId}", operationId);
            HostLogger.ZLogInformation(
                $"Video recording started. OperationId={operationId} SystemAudio={systemAudio} Microphone={microphone} HasPreset={preset is not null}");
            try
            {
                var monitors = Screens.All.Select(screen => new PixelRect(screen.Bounds.X, screen.Bounds.Y,
                    screen.Bounds.Width, screen.Bounds.Height)).ToArray();
                if (DesktopCapture.UsesSourceSelection &&
                    preset?.Layout?.Layers.All(layer => layer.Media is not null) != true)
                {
                    using var prepare = CancellationTokenSource.CreateLinkedTokenSource(stop, cancel);
                    await DesktopCapture.PrepareSourceAsync(preset?.Source == CaptureSourceKind.Window ||
                                                            (preset is null && NativeCaptureSession.SourceIsWindow),
                        options.Capture.Cursor,
                        options.Capture.Fps, prepare.Token);
                    monitors = [DesktopCapture.SourceBounds ?? throw new CaptureSourceLostException()];
                }

                using var displays = DesktopCapture.UsesSourceSelection ? null : new DesktopDisplayState(Screens);
                var topology = new DisplayTopology(monitors);
                Func<DisplayTopology> currentDisplay = displays is null ? () => topology : displays.Read;
                var toneMap = options.ToneMap;
                var runtime = _presets.ActivePreset == preset ? _presets.Runtime : null;

                bool published;
                using (var progress = new RecordingProgressPresenter(value => UpdateRecordingProgress(value, false)))
                    published = await Task.Run(() => new VideoRecordingSession(CreateSource, CreateAudio)
                        .RunAsync(options, path, progress, stop, cancel), cancel);
                if (!published) throw new OperationCanceledException(stop);
                LastPath = path;
                LastOutputs = [.. options.GetOutputPaths(path)];
                if (retainOutput) QueueCleanup(LastOutputs);
                SetStatus(UiText.Format("VideoSaved", path));
                return new();

                IFrameSource CreateSource(RecordingOptions capture)
                {
                    IFrameSource source = preset is null
                        ? new FollowingCapture(capture, currentDisplay, () =>
                            Volatile.Read(ref _captureRegion) is { } following
                                ? following.Resolve()
                                : options.Capture.Region)
                        : new SceneCapture(capture, currentDisplay, preset, runtime, inputs: _inputs);
                    if (options.ColorMode != OutputColorMode.Sdr || toneMap == HdrToneMapMode.HighlightCompression)
                        return source;
                    if (source is IColorFrameSource color) return new ToneMappedFrameSource(color, toneMap);
                    source.Dispose();
                    throw new NotSupportedException(UiText.Get("HdrRecordingUnavailable"));
                }

                IReadOnlyList<IAudioCaptureSource> CreateAudio()
                {
                    var sources = new List<IAudioCaptureSource>();
                    try
                    {
                        if (systemAudio) sources.Add(WindowsAudioCapture.Create(true, systemDevice));
                        if (microphone) sources.Add(WindowsAudioCapture.Create(false, microphoneDevice));
                        return sources;
                    }
                    catch
                    {
                        foreach (var source in sources) source.Dispose();
                        throw;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                HostLogger.ZLogInformation($"Video recording cancelled. OperationId={operationId}");
                SetStatusKey("Cancelled");
                return new(true);
            }
            catch (Exception error)
            {
                HostLogger.ZLogError(error, $"Video recording failed. OperationId={operationId}");
                SetStatus(UiText.Get("RecordingFailed") + VideoErrorMessage(error));
                return new(Error: error);
            }
            finally
            {
                HostLogger.ZLogInformation($"Video recording ended. OperationId={operationId} Closing={IsClosing}");
                _presets.CompleteVideoRecording(preset);
                _recordSettings = null;
                _manualCaptureCompleted = false;
                _recordClock.Stop();
                IsRecording = IsBusy = false;
                _stop?.Dispose();
                _abort?.Dispose();
                _stop = _abort = null;
                if (!IsClosing && View is { } view)
                    try
                    {
                        await view.ResumePreviewAsync();
                    }
                    catch (Exception error)
                    {
                        SetStatus(UiText.Get("PreviewFailed") + error.Message);
                    }

                RefreshDisplaySelection();
                Notify();
            }
        }

        internal void SaveVideoPreferences(VideoRecordingPreferences preferences)
        {
            var updated = Settings with { VideoRecording = preferences };
            QueueSettingsSave(updated);
            Settings = updated;
        }
    }
}

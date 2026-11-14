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

            else
            {
                _videoRecorder.SelectVideoOutput();
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
            if (IsClosing || IsBusy || IsSelecting || IsConfiguring || _screenshotRecordingPreparing || Region is null)
                throw new InvalidOperationException(UiText.Get("CommandNotReady"));
            var preset = _presets.ActivePreset ?? _manualPreset;
            if (preset?.Layout is { } layout)
                options = options with
                {
                    Capture = options.Capture with { Region = new(0, 0, layout.Width, layout.Height) },
                };
            if ((preset is null || ReferenceEquals(preset, _manualPreset)) && preset?.Layout is null)
                options = options with
                {
                    Capture = options.Capture with
                    {
                        PixelDensity = NativeDisplayCatalog.Density(options.Capture.Region),
                    },
                };
            options.Validate();
            if (FfmpegEncoder.FindExecutable() is null)
                throw new FfmpegDependencyException(UiText.Get("FfmpegMissing"));
            if ((systemAudio || microphone) && !AudioCaptureBackend.IsSupported)
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
            BeginRecordingControl();
            _recordClock.Restart();
            Notify();
            _operation = RecordVideoAsync(options, path, preset, systemAudio, systemDevice, microphone,
                microphoneDevice, retainOutput, _stop.Token, _abort.Token);
            return _operation;
        }

        internal async Task StopOwnedVideoAsync(Task<RecordingOutcome>? operation, bool cancel)
        {
            if (operation is null || operation.IsCompleted || !ReferenceEquals(operation, _operation)) return;
            if (cancel)
            {
                CancelRecording();
            }
            else if (_screenshotRecordingActive)
            {
                IsRecording = false;
                if (_stop is { } stop) await stop.CancelAsync();
                Notify();
            }
            else if (IsRecording)
            {
                await ToggleRecordingAsync();
            }

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
            var sourceLoss = _presets.ActivePreset is null ? ObserveWindowSourceLoss() : null;
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
                        options.Capture.Fps, Settings.CaptureColorMode != OutputColorMode.Sdr, prepare.Token);
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
                        .RunAsync(options, path, progress, stop, cancel, _recordingControl), cancel);
                if (!published) throw new OperationCanceledException(stop);
                var outputs = options.GetOutputPaths(path).ToArray();
                LastPath = path;
                LastOutputs = outputs;
                if (retainOutput) QueueCleanup(outputs);
                SetStatus(UiText.Format("VideoSaved", path));
                return new() { Outputs = outputs };

                IFrameSource CreateSource(RecordingOptions capture)
                {
                    IFrameSource source = preset is null
                        ? new FollowingCapture(capture, currentDisplay, () =>
                            Volatile.Read(ref _captureRegion) is { } following
                                ? following.Resolve()
                                : options.Capture.Region)
                        {
                            SourceLost = sourceLoss is not null && DesktopCapture.UsesSourceSelection &&
                                         NativeCaptureSession.SourceIsWindow
                                ? sourceLoss.Report
                                : null,
                        }
                        : new SceneCapture(capture, currentDisplay, preset, runtime, inputs: _inputs)
                            { WindowSourceLost = sourceLoss is null ? null : sourceLoss.Report };
                    if (options.ColorMode != OutputColorMode.Sdr || toneMap == HdrToneMapMode.HighlightCompression)
                        return source;
                    if (source is IColorFrameSource color) return new ToneMappedFrameSource(color, toneMap);
                    source.Dispose();
                    throw new NotSupportedException(UiText.Get("HdrRecordingUnavailable"));
                }

                IReadOnlyList<IAudioCaptureSource> CreateAudio()
                {
                    using var preparing = CancellationTokenSource.CreateLinkedTokenSource(stop, cancel);
                    var sources = new List<IAudioCaptureSource>();
                    try
                    {
                        if (systemAudio) sources.Add(AudioCaptureBackend.Create(true, systemDevice, preparing.Token));
                        if (microphone)
                            sources.Add(AudioCaptureBackend.Create(false, microphoneDevice, preparing.Token));
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
                CompleteRecordingControl();
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
                if (!IsClosing && sourceLoss?.WasLost == true) ShowWindowSourceLost();
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

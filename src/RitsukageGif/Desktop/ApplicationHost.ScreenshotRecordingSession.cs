using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private bool _screenshotRecordingActive;

        internal Task<RecordingOutcome> StartScreenshotAnimationRecordingAsync(SelectionResult selection,
            RecordingOptions options, string temporaryDirectory, bool recordKeyboardActions, bool recordMouseActions,
            RecordingControl? control = null)
        {
            Dispatcher.UIThread.VerifyAccess();
            if (IsClosing || IsBusy || IsRecording) return Task.FromResult(new RecordingOutcome(true));
            var operationReference = selection.CaptureSession?.Retain();
            var extension = options.Format switch
            {
                OutputFormat.WebP => ".webp",
                OutputFormat.APng => ".png",
                _ => ".gif",
            };
            var path = Path.Combine(temporaryDirectory, "capture" + extension);
            return StartScreenshotRecording(loss => RecordScreenshotAnimationAsync(selection, options, path, loss,
                recordKeyboardActions, recordMouseActions), control, operationReference);
        }

        internal Task<RecordingOutcome> StartScreenshotVideoRecordingAsync(SelectionResult selection,
            VideoRecordingOptions options, string path, bool systemAudio, string? systemDevice,
            bool microphone, string? microphoneDevice, bool editable, bool recordKeyboardActions,
            bool recordMouseActions,
            RecordingControl? control = null)
        {
            Dispatcher.UIThread.VerifyAccess();
            if (IsClosing || IsBusy || IsRecording) return Task.FromResult(new RecordingOutcome(true));
            var operationReference = selection.CaptureSession?.Retain();
            return StartScreenshotRecording(loss => RecordScreenshotVideoAsync(selection, options, path,
                systemAudio, systemDevice, microphone, microphoneDevice, editable, loss,
                recordKeyboardActions, recordMouseActions), control, operationReference);
        }

        private Task<RecordingOutcome> StartScreenshotRecording(Func<WindowSourceLoss, Task<RecordingOutcome>> run,
            RecordingControl? control, NativeCaptureSessionReference? operationReference)
        {
            try
            {
                _screenshotRecordingActive = true;
                LatestProgress = null;
                IsBusy = IsRecording = true;
                _stop = new();
                _abort = new();
                BeginRecordingControl(control);
                _recordClock.Restart();
                Notify();
                _operation = CompleteScreenshotRecordingAsync(run, operationReference);
                return _operation;
            }
            catch
            {
                operationReference?.Dispose();
                throw;
            }
        }

        private async Task<RecordingOutcome> CompleteScreenshotRecordingAsync(
            Func<WindowSourceLoss, Task<RecordingOutcome>> run, NativeCaptureSessionReference? operationReference)
        {
            await Task.Yield();
            var operationId = Interlocked.Increment(ref _nextOperationId);
            var loss = new WindowSourceLoss(_stop!, () => HostLogger.ZLogWarning($"Screenshot recording source lost."));
            using var scope = HostLogger.BeginScope("ScreenshotRecording {OperationId}", operationId);
            HostLogger.ZLogInformation($"Screenshot recording started. OperationId={operationId}");
            try
            {
                return await run(loss);
            }
            catch (OperationCanceledException)
            {
                HostLogger.ZLogInformation($"Screenshot recording cancelled. OperationId={operationId}");
                return new(true) { SourceLost = loss.WasLost };
            }
            catch (CaptureSourceLostException error)
            {
                loss.Report();
                HostLogger.ZLogWarning(error, $"Screenshot recording target is unavailable. OperationId={operationId}");
                return new(Error: error) { SourceLost = true };
            }
            catch (Exception error)
            {
                HostLogger.ZLogError(error, $"Screenshot recording failed. OperationId={operationId}");
                return new(Error: error) { SourceLost = loss.WasLost };
            }
            finally
            {
                CompleteRecordingControl();
                _recordClock.Stop();
                IsRecording = IsBusy = false;
                _screenshotRecordingActive = false;
                _stop?.Dispose();
                _abort?.Dispose();
                _stop = _abort = null;
                operationReference?.Dispose();
                Notify();
                HostLogger.ZLogInformation($"Screenshot recording resources released. OperationId={operationId}");
            }
        }

        private async Task<RecordingOutcome> RecordScreenshotAnimationAsync(SelectionResult selection,
            RecordingOptions options, string path, WindowSourceLoss loss,
            bool recordKeyboardActions, bool recordMouseActions)
        {
            var stop = _stop!.Token;
            var cancel = _abort!.Token;
            var colorMode = Settings.CaptureColorMode;
            var toneMap = Settings.CaptureToneMap;
            var hdrPath = Path.Combine(Path.GetDirectoryName(path)!,
                Path.GetFileNameWithoutExtension(path) + ".hdr.png");
            using var preparing = CancellationTokenSource.CreateLinkedTokenSource(stop, cancel);
            using var context = await ScreenshotCaptureSourceContext.CreateAsync(selection,
                options with { Cursor = false }, Screens, loss, preparing.Token,
                CaptureOutputScale(selection.Region, DpiOutputFeature.ScreenshotRecording, selection.CaptureSession));
            _screenshotRecorder?.SetRecordingNativeSize(context.Options);
            using var progress = new RecordingProgressPresenter(value =>
            {
                LatestProgress = value;
                Notify();
            });
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            await using var trace = new RecordingInputTraceCollector(
                colorMode == OutputColorMode.Hdr ? hdrPath : path, _recordingControl!,
                context.GetInputGeometry, recordKeyboardActions, recordMouseActions,
                DesktopCapture.UsesSourceSelection ? context.GetCursorFrame : null,
                // ReSharper disable once AccessToDisposedClosure
                DesktopCapture.UsesSourceSelection ? () => context.CursorUnavailableReason : null,
                ScreenshotRecordingInputTracks);
            await trace.StartAsync(preparing.Token);
            if (trace.UnavailableReason is { } reason)
                HostLogger.ZLogWarning($"Screenshot recording input observation unavailable. Reason={reason}");
            var annotations = ScreenshotRecordingAnnotations;
            var camera = ScreenshotRecordingCameraSession;
            IReadOnlyList<string> outputs;
            if (colorMode == OutputColorMode.Sdr)
            {
                await Task.Run(() => new RecordingSession(capture =>
                {
                    IFrameSource source =
                        // ReSharper disable once AccessToDisposedClosure
                        new RecordingInputCaptureSource(context.CreateSource(capture), capture,
                            // ReSharper disable once AccessToDisposedClosure
                            trace);
                    if (annotations is not null || camera is not null)
                        source = new RecordingAnnotationFrameSource(source, annotations, capture.Region, camera);
                    var finalSource = toneMap == HdrToneMapMode.HighlightCompression
                        ? source
                        : new ToneMappedFrameSource((IColorFrameSource)source, toneMap);
                    return finalSource;
                }).RunAsync(context.Options, path, progress, stop, cancel, _recordingControl), cancel);
                outputs = [path];
            }
            else
            {
                using var source = CreateColorSource();
                await ColorRecordingSession.RunAsync((IColorFrameSource)source,
                    new(context.Options, colorMode == OutputColorMode.Both ? path : null, hdrPath,
                        ToneMap: toneMap), progress, stop, cancel, _recordingControl);
                outputs = colorMode == OutputColorMode.Both ? [path, hdrPath] : [hdrPath];
            }

            var statistics = _recordingControl!.GetStatistics()!;
            return new()
            {
                Outputs = outputs, OutputsAreTemporary = true, SourceLost = loss.WasLost,
                RecordedDuration = statistics.MediaDuration, AcceptedCaptures = statistics.AcceptedCaptures,
            };

            IFrameSource CreateColorSource()
            {
                IFrameSource source = new RecordingInputCaptureSource(context.CreateSource(context.Options),
                    context.Options, trace);
                return annotations is null && camera is null
                    ? source
                    : new RecordingAnnotationFrameSource(source, annotations, context.Options.Region, camera);
            }
        }

        private async Task<RecordingOutcome> RecordScreenshotVideoAsync(SelectionResult selection,
            VideoRecordingOptions options, string path, bool systemAudio, string? systemDevice,
            bool microphone, string? microphoneDevice, bool editable, WindowSourceLoss loss,
            bool recordKeyboardActions, bool recordMouseActions)
        {
            var stop = _stop!.Token;
            var cancel = _abort!.Token;
            using var preparing = CancellationTokenSource.CreateLinkedTokenSource(stop, cancel);
            using var context = await ScreenshotCaptureSourceContext.CreateAsync(selection,
                options.Capture with { Cursor = !editable && options.Capture.Cursor }, Screens,
                loss, preparing.Token,
                CaptureOutputScale(selection.Region, DpiOutputFeature.VideoRecording, selection.CaptureSession));
            options = options with { Capture = context.Options };
            if (editable)
            {
                var audioTracks = new List<VideoAudioTrackKind>(2);
                if (systemAudio) audioTracks.Add(VideoAudioTrackKind.SystemAudio);
                if (microphone) audioTracks.Add(VideoAudioTrackKind.Microphone);
                options = options with { AudioTrackKinds = audioTracks };
            }

            _screenshotRecorder?.SetRecordingNativeSize(context.Options);
            using var progress = new RecordingProgressPresenter(value =>
            {
                LatestProgress = value;
                Notify();
            });
            if (editable) Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            await using var trace = editable
                ? new RecordingInputTraceCollector(path, _recordingControl!, context.GetInputGeometry,
                    recordKeyboardActions, recordMouseActions,
                    DesktopCapture.UsesSourceSelection ? context.GetCursorFrame : null,
                    // ReSharper disable once AccessToDisposedClosure
                    DesktopCapture.UsesSourceSelection ? () => context.CursorUnavailableReason : null,
                    ScreenshotRecordingInputTracks)
                : null;
            if (trace is not null)
            {
                await trace.StartAsync(preparing.Token);
                if (trace.UnavailableReason is { } reason)
                    HostLogger.ZLogWarning($"Screenshot recording input observation unavailable. Reason={reason}");
            }

            var annotations = ScreenshotRecordingAnnotations;
            var camera = ScreenshotRecordingCameraSession;
            var published = await Task.Run(() => new VideoRecordingSession(CreateSource, CreateAudio)
                .RunAsync(options, path, progress, stop, cancel, _recordingControl), cancel);
            var statistics = _recordingControl!.GetStatistics();
            return new(!published)
            {
                Outputs = published ? options.GetOutputPaths(path) : [], OutputsAreTemporary = editable,
                SourceLost = loss.WasLost,
                RecordedDuration = statistics?.MediaDuration ?? TimeSpan.Zero,
                AcceptedCaptures = statistics?.AcceptedCaptures ?? 0,
            };

            IFrameSource CreateSource(RecordingOptions capture)
            {
                var source = context.CreateSource(capture);
                if (trace is not null) source = new RecordingInputCaptureSource(source, capture, trace);
                if (annotations is not null || camera is not null)
                    source = new RecordingAnnotationFrameSource(source, annotations, capture.Region, camera);
                var finalSource = options.ColorMode == OutputColorMode.Sdr &&
                                  options.ToneMap != HdrToneMapMode.HighlightCompression
                    ? new ToneMappedFrameSource((IColorFrameSource)source, options.ToneMap)
                    : source;
                return finalSource;
            }

            IReadOnlyList<IAudioCaptureSource> CreateAudio()
            {
                var sources = new List<IAudioCaptureSource>();
                try
                {
                    if (systemAudio) sources.Add(AudioCaptureBackend.Create(true, systemDevice, preparing.Token));
                    if (microphone) sources.Add(AudioCaptureBackend.Create(false, microphoneDevice, preparing.Token));
                    return sources;
                }
                catch
                {
                    foreach (var source in sources) source.Dispose();
                    throw;
                }
            }
        }
    }
}

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        public async Task ToggleRecordingAsync()
        {
            HostLogger.ZLogInformation(
                $"Recording toggle requested. Recording={IsRecording} Busy={IsBusy} Selecting={IsSelecting} Closing={IsClosing}");
            if (IsClosing) return;
            if (IsRecording)
            {
                IsRecording = false;
                if (_stop is { } stop) await stop.CancelAsync();
                _presets.NotifyRecordingChanged();
                (_recordSettings ?? Settings).Play(false, SetStatus);
                Notify();
                return;
            }

            if (IsSelecting || IsConfiguring || IsBusy || Region is not { } region) return;
            View?.SaveCommon();
            View?.PausePreview();
            LatestProgress = null;
            _manualCaptureCompleted = false;
            _streams = [];
            IsBusy = true;
            IsRecording = true;
            _recordClock.Restart();
            Notify();
            _stop = new();
            _abort = new();
            var overrides = _startOverrides;
            _startOverrides = null;
            _operation = RecordAsync(region, _presets.ActivePreset ?? _manualPreset,
                overrides, _stop.Token, _abort.Token);
            await _operation;
        }

        private void UpdateRecordingProgress(RecordingProgress progress, bool manualRecording)
        {
            if (IsClosing) return;
            LatestProgress = progress;
            if (manualRecording && progress.Stage is RecordingStage.Encoding or RecordingStage.Complete)
                _manualCaptureCompleted = true;
            SetStatusKey("Stage" + progress.Stage);
        }

        private async Task<RecordingOutcome> RecordAsync(PixelRect region,
            WindowPreset? preset, PresetRecordingSettings? overrides, CancellationToken stop, CancellationToken abort)
        {
            var operationId = Interlocked.Increment(ref _nextOperationId);
            using var scope = HostLogger.BeginScope("RecordingOperation {RecordingOperationId}", operationId);
            HostLogger.ZLogInformation(
                $"Recording operation started. OperationId={operationId} Region={region} HasPreset={preset is not null}");
            var manualRecording = _presets.ActivePreset is null &&
                                  (preset is null || ReferenceEquals(preset, _manualPreset));
            try
            {
                var effective = preset?.Settings ?? new PresetRecordingSettings();
                if (overrides is not null) effective = effective.Overlay(overrides);
                var settings = Settings with
                {
                    Fps = effective.Fps ?? Settings.Fps,
                    Divisor = effective.Scale ?? Settings.Divisor,
                    Format = effective.Format ?? Settings.Format,
                    Quality = effective.Quality ?? Settings.Quality, Cursor = effective.Cursor ?? Settings.Cursor,
                    Sound = effective.Sound ?? Settings.Sound, UseFfmpeg = effective.Ffmpeg ?? Settings.UseFfmpeg,
                };
                _recordSettings = settings;
                Notify();
                if (preset?.Layout is { } layout) region = new(0, 0, layout.Width, layout.Height);
                var options = new RecordingOptions(region, settings.Fps, settings.Divisor, settings.Cursor,
                    settings.Format, settings.Quality, settings.UseFfmpeg, preset is not null);
                options.Validate();
                var extension =
                    options.Format switch { OutputFormat.WebP => ".webp", OutputFormat.APng => ".png", _ => ".gif" };
                var outputs = BuildOutputPaths(preset, extension, ++_recordSequence);
                List<string>? hdrOutputs = null;
                if (settings.CaptureColorMode != OutputColorMode.Sdr)
                    hdrOutputs =
                    [
                        .. outputs.Select(output => Path.Combine(Path.GetDirectoryName(output)!,
                            Path.GetFileNameWithoutExtension(output) + ".hdr.png")),
                    ];
                var path = outputs[0];
                var progress = new RecordingProgressPresenter(p => UpdateRecordingProgress(p, manualRecording));
                try
                {
                    settings.Play(true, SetStatus);
                    var monitors = Screens.All
                        .Select(s => new PixelRect(s.Bounds.X, s.Bounds.Y, s.Bounds.Width, s.Bounds.Height)).ToArray();
                    if (DesktopCapture.UsesSourceSelection &&
                        preset?.Layout?.Layers.All(layer => layer.Media is not null) != true)
                    {
                        var windowSource = preset is null
                            ? NativeCaptureSession.SourceIsWindow
                            : (preset.Layout?.Layers is { Count: > 0 } layers ? layers[0].Source : preset.Source) ==
                              CaptureSourceKind.Window;
                        using var prepareCancellation = CancellationTokenSource.CreateLinkedTokenSource(stop, abort);
                        await DesktopCapture.PrepareSourceAsync(windowSource, settings.Cursor, settings.Fps,
                            prepareCancellation.Token);
                        monitors = [DesktopCapture.SourceBounds ?? throw new CaptureSourceLostException()];
                    }

                    using var displays = DesktopCapture.UsesSourceSelection ? null : new DesktopDisplayState(Screens);
                    var sourceTopology = new DisplayTopology(monitors);
                    Func<DisplayTopology> currentDisplay = displays is null ? () => sourceTopology : displays.Read;

                    PixelRect? CurrentRegion()
                    {
                        return Volatile.Read(ref _captureRegion) is { } following
                            ? following.Resolve()
                            : region;
                    }

                    var runtime = _presets.ActivePreset == preset ? _presets.Runtime : null;

                    IFrameSource CreateSource(RecordingOptions captureOptions)
                    {
                        return preset is not null
                            ? new SceneCapture(captureOptions, currentDisplay, preset, runtime, inputs: _inputs)
                            : new FollowingCapture(captureOptions, currentDisplay, CurrentRegion);
                    }

                    if (hdrOutputs is not null)
                    {
                        using var source = CreateSource(options);
                        if (source is not IColorFrameSource colorSource)
                            throw new NotSupportedException(UiText.Get("HdrRecordingUnavailable"));
                        var result = await ColorRecordingSession.RunAsync(colorSource,
                            new(options, settings.CaptureColorMode == OutputColorMode.Both ? path : null,
                                hdrOutputs[0], ToneMap: settings.CaptureToneMap), progress, stop, abort);
                        hdrOutputs[0] = result.HdrPath;
                        if (result.SdrPath is { } sdrPath) outputs[0] = path = sdrPath;
                        progress.Report(new(RecordingStage.Complete, result.CapturedFrames, result.CapturedFrames,
                            result.Duration.TotalSeconds > 0 ? result.CapturedFrames / result.Duration.TotalSeconds : 0,
                            result.Duration.TotalSeconds));
                    }
                    else
                    {
                        await new RecordingSession(o =>
                        {
                            var source = CreateSource(o);
                            if (settings.CaptureToneMap == HdrToneMapMode.HighlightCompression) return source;
                            if (source is IColorFrameSource colorSource)
                                return new ToneMappedFrameSource(colorSource, settings.CaptureToneMap);
                            source.Dispose();
                            throw new NotSupportedException(UiText.Get("HdrRecordingUnavailable"));
                        }).RunAsync(options, path, progress, stop, abort);
                    }
                }
                finally
                {
                    progress.Dispose();
                }

                if (hdrOutputs is null)
                {
                    await CopyOutputsAsync(path, outputs, abort);
                }
                else
                {
                    var published = new List<string> { hdrOutputs[0] };
                    if (settings.CaptureColorMode == OutputColorMode.Both) published.Add(path);
                    try
                    {
                        if (settings.CaptureColorMode == OutputColorMode.Both)
                        {
                            await CopyOutputsAsync(path, outputs, abort);
                            published.AddRange(outputs.Skip(1));
                        }

                        await CopyOutputsAsync(hdrOutputs[0], hdrOutputs, abort);
                        published.AddRange(hdrOutputs.Skip(1));
                        abort.ThrowIfCancellationRequested();
                    }
                    catch
                    {
                        foreach (var publishedPath in published) File.Delete(publishedPath);
                        throw;
                    }

                    if (settings.CaptureColorMode == OutputColorMode.Hdr)
                    {
                        outputs = hdrOutputs;
                        path = outputs[0];
                    }
                    else
                    {
                        outputs.AddRange(hdrOutputs);
                    }
                }

                LastPath = path;
                LastOutputs = outputs;
                SetStatusKey("RecordingComplete");
                try
                {
                    if (effective.Copy ?? true) await CopyAsync();
                }
                catch (Exception ex)
                {
                    SetStatus(UiText.Get("ClipboardFailed") + ex.Message);
                }

                try
                {
                    if (View is { } view) await view.LoadPreviewAsync(path);
                }
                catch (Exception ex)
                {
                    SetStatus(UiText.Get("PreviewFailed") + ex.Message);
                }

                if (manualRecording) QueueCleanup(outputs);
                HostLogger.ZLogInformation(
                    $"Recording outputs completed. OperationId={operationId} OutputCount={outputs.Count} ElapsedSeconds={_recordClock.Elapsed.TotalSeconds}");
                _presets.Signal("saved");
                return new();
            }
            catch (OperationCanceledException)
            {
                HostLogger.ZLogInformation($"Recording operation cancelled. OperationId={operationId}");
                SetStatusKey("Cancelled");
                _presets.Signal("cancelled");
                return new(true);
            }
            catch (Exception ex)
            {
                HostLogger.ZLogError(ex, $"Recording operation failed. OperationId={operationId}");
                SetStatus(UiText.Get("RecordingFailed") + ex.Message);
                _presets.Signal("error");
                return new(Error: ex);
            }
            finally
            {
                _recordSettings = null;
                _manualCaptureCompleted = false;
                _recordClock.Stop();
                IsRecording = IsBusy = false;
                _stop?.Dispose();
                _abort?.Dispose();
                _stop = _abort = null;
                if (!IsClosing && View is { } view) await view.ResumePreviewAsync();
                RefreshDisplaySelection();
                Notify();
                HostLogger.ZLogDebug($"Recording resources released. OperationId={operationId} Closing={IsClosing}");
            }
        }
    }
}

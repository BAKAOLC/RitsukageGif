namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        public async Task ToggleRecordingAsync()
        {
            if (_closing || _selecting || _configuring) return;
            if (_recording)
            {
                _recording = false;
                _stop?.Cancel();
                _presets.NotifyRecordingChanged();
                (_recordSettings ?? _settings).Play(false, SetStatus);
                Notify();
                return;
            }

            if (_busy || _region is not { } region) return;
            View?.SaveCommon();
            View?.PausePreview();
            _latestProgress = null;
            _manualCaptureCompleted = false;
            _streams = [];
            _busy = true;
            _recording = true;
            _recordClock.Restart();
            Notify();
            _stop = new CancellationTokenSource();
            _abort = new CancellationTokenSource();
            var overrides = _startOverrides;
            _startOverrides = null;
            _operation = RecordAsync(region, _stop.Token, _abort.Token, _presets.ActivePreset ?? _manualPreset,
                overrides);
            await _operation;
        }

        private void UpdateRecordingProgress(RecordingProgress progress, bool manualRecording)
        {
            if (_closing) return;
            _latestProgress = progress;
            if (manualRecording && progress.Stage is RecordingStage.Encoding or RecordingStage.Complete)
                _manualCaptureCompleted = true;
            SetStatus(UiText.Get("Stage" + progress.Stage));
        }

        private async Task<RecordingOutcome> RecordAsync(PixelRect region, CancellationToken stop,
            CancellationToken abort,
            WindowPreset? preset, PresetRecordingSettings? overrides)
        {
            var manualRecording = _presets.ActivePreset is null &&
                                  (preset is null || ReferenceEquals(preset, _manualPreset));
            try
            {
                var effective = preset?.Settings ?? new PresetRecordingSettings();
                if (overrides is not null) effective = effective.Overlay(overrides);
                var settings = _settings with
                {
                    Fps = effective.Fps ?? _settings.Fps,
                    Divisor = effective.Scale ?? _settings.Divisor,
                    Format = effective.Format ?? _settings.Format,
                    Quality = effective.Quality ?? _settings.Quality, Cursor = effective.Cursor ?? _settings.Cursor,
                    Sound = effective.Sound ?? _settings.Sound, UseFfmpeg = effective.Ffmpeg ?? _settings.UseFfmpeg
                };
                _recordSettings = settings;
                Notify();
                if (preset?.Layout is { } layout) region = new PixelRect(0, 0, layout.Width, layout.Height);
                var options = new RecordingOptions(region, settings.Fps, settings.Divisor, settings.Cursor,
                    settings.Format, settings.Quality, settings.UseFfmpeg, preset is not null);
                options.Validate();
                var extension =
                    options.Format switch { OutputFormat.WebP => ".webp", OutputFormat.APng => ".png", _ => ".gif" };
                var outputs = BuildOutputPaths(preset, extension, ++_recordSequence);
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
                            : (preset.Layout?.Layers.FirstOrDefault()?.Source ?? preset.Source) ==
                              CaptureSourceKind.Window;
                        using var prepareCancellation = CancellationTokenSource.CreateLinkedTokenSource(stop, abort);
                        await DesktopCapture.PrepareSourceAsync(windowSource, prepareCancellation.Token,
                            settings.Cursor,
                            settings.Fps);
                        monitors = [DesktopCapture.SourceBounds ?? throw new CaptureSourceLostException()];
                    }

                    using var displays = DesktopCapture.UsesSourceSelection ? null : new DesktopDisplayState(Screens);
                    var sourceTopology = new DisplayTopology(monitors);
                    Func<DisplayTopology> currentDisplay = displays is null ? () => sourceTopology : displays.Read;

                    PixelRect? CurrentRegion() => Volatile.Read(ref _captureRegion) is { } following
                        ? following.Resolve()
                        : region;

                    var runtime = _presets.ActivePreset == preset ? _presets.Runtime : null;
                    await new RecordingSession(o =>
                            preset is not null
                                ? new SceneCapture(o, currentDisplay, preset, runtime, inputs: _inputs)
                                : new FollowingCapture(o, currentDisplay, CurrentRegion))
                        .RunAsync(
                            options, path, progress, stop,
                            abort);
                }
                finally
                {
                    progress.Dispose();
                }

                await CopyOutputsAsync(path, outputs, abort);
                _lastPath = path;
                _lastOutputs = outputs;
                SetStatus(UiText.Get("RecordingComplete"));
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
                _presets.Signal("saved");
                return new RecordingOutcome();
            }
            catch (OperationCanceledException)
            {
                SetStatus(UiText.Get("Cancelled"));
                _presets.Signal("cancelled");
                return new RecordingOutcome(Cancelled: true);
            }
            catch (Exception ex)
            {
                SetStatus(UiText.Get("RecordingFailed") + ex.Message);
                _presets.Signal("error");
                return new RecordingOutcome(Error: ex);
            }
            finally
            {
                _recordSettings = null;
                _manualCaptureCompleted = false;
                _recordClock.Stop();
                _recording = _busy = false;
                _stop?.Dispose();
                _abort?.Dispose();
                _stop = _abort = null;
                if (!_closing && View is { } view) await view.ResumePreviewAsync();
                RefreshDisplaySelection();
                Notify();
            }
        }
    }
}

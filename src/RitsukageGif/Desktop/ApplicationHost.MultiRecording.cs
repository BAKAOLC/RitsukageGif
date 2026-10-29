namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private IReadOnlyList<PresetRecordingStreamStatus> _streams = [];
        public IReadOnlyList<PresetRecordingStreamStatus> Streams => Volatile.Read(ref _streams);

        private void StartMultiple(IReadOnlyList<PresetRecordingOutput> definitions)
        {
            if (Settings.CaptureColorMode != OutputColorMode.Sdr ||
                Settings.CaptureToneMap != HdrToneMapMode.HighlightCompression)
                throw new NotSupportedException(UiText.Get("HdrMultipleUnsupported"));
            if (IsBusy || IsSelecting || IsConfiguring || IsClosing || definitions.Count is < 1 or > 8)
                throw new InvalidOperationException(UiText.Get("CommandNotReady"));
            var needsDesktop = definitions.Any(output => output.Preset?.Layout is not { } layout ||
                                                         layout.Layers.Any(layer => layer.Media is null));
            if (DesktopCapture.UsesSourceSelection && needsDesktop)
                throw new PlatformNotSupportedException(UiText.Get("CapturePlatformUnsupported"));
            View?.SaveCommon();
            var sequence = ++_recordSequence;
            var timestamp = DateTime.Now;
            var windows = needsDesktop ? WindowLocator.List() : [];
            var outputs = new List<MultiRecordingOutput>();
            var sources = new Dictionary<string, PresetRecordingOutput>(StringComparer.Ordinal);
            foreach (var definition in definitions)
            {
                var preset = definition.Preset ?? _presets.ActivePreset ??
                    throw new ArgumentException(UiText.Get("InvalidRecording"));
                var settings = preset.Settings.Overlay(definition.Settings ?? new PresetRecordingSettings());
                var target = preset.Absolute || preset.Layout?.Layers.All(layer => layer.Media is not null) == true
                    ? null
                    : WindowLocator.Find(preset, windows);
                var region = preset.Layout is { } layout
                    ? new(0, 0, layout.Width, layout.Height)
                    : preset.Resolve(target?.Bounds ?? default, target?.Client ?? default);
                var options = new RecordingOptions(region, settings.Fps ?? Settings.Fps,
                    settings.Scale ?? Settings.Divisor, settings.Cursor ?? Settings.Cursor,
                    settings.Format ?? Settings.Format, settings.Quality ?? Settings.Quality,
                    settings.Ffmpeg ?? Settings.UseFfmpeg, true);
                options.Validate();
                var extension = options.Format switch
                {
                    OutputFormat.WebP => ".webp", OutputFormat.APng => ".png", _ => ".gif",
                };
                var path = Path.Combine(Path.GetDirectoryName(definition.Path)!,
                    PresetOutputNaming.Filename(Path.GetFileName(definition.Path), preset.Name,
                        target?.Title ?? preset.Title, target?.Process ?? preset.Process, extension, sequence,
                        timestamp));
                outputs.Add(new(definition.Name, options, path));
                sources.Add(definition.Name, definition with { Preset = preset, Path = path });
            }

            if (outputs.Select(output => output.Path).Distinct(OperatingSystem.IsWindows()
                    ? StringComparer.OrdinalIgnoreCase
                    : StringComparer.Ordinal).Count() != outputs.Count)
                throw new ArgumentException(UiText.Get("InvalidRecording"));
            if (outputs.Any(output => File.Exists(output.Path)))
                throw new IOException(UiText.Get("DslOutputInvalid"));
            _captureDefinitions = sources;
            View?.PausePreview();
            LatestProgress = null;
            _streams =
            [
                .. outputs.Select(output =>
                    new PresetRecordingStreamStatus(output.Name, "recording", 0, 0, 0, output.Path)),
            ];
            IsBusy = IsRecording = true;
            _recordClock.Restart();
            _stop = new();
            _abort = new();
            _operation = RecordMultipleAsync(outputs, sources, _stop.Token, _abort.Token);
            Notify();
        }

        private async Task<RecordingOutcome> RecordMultipleAsync(List<MultiRecordingOutput> outputs,
            Dictionary<string, PresetRecordingOutput> sources, CancellationToken stop,
            CancellationToken cancel)
        {
            var gate = new Lock();
            var finished = false;
            // Disposal flushes the last update; the completion guard must observe the final value.
            // ReSharper disable once AccessToModifiedClosure
            using var progress = new RecordingProgressPresenter(value =>
            {
                if (IsClosing || finished) return;
                LatestProgress = value;
                SetStatusKey("Stage" + value.Stage);
            });
            var sink = new MultiRecordingProgressSink(value =>
            {
                lock (gate)
                {
                    var updated = _streams.Select(stream => stream.Name == value.Name
                        ? stream with
                        {
                            State = value.Error is not null ? "failed" :
                            value.Progress.Stage is RecordingStage.Recording or RecordingStage.Live ? "recording" :
                            "encoding",
                            Captured = value.Progress.Captured, Encoded = value.Progress.Encoded,
                            Elapsed = value.Progress.ElapsedSeconds, Error = value.Error,
                        }
                        : stream).ToArray();
                    Volatile.Write(ref _streams, updated);
                    // RunAsync awaits every producer before the presenter is disposed.
                    // ReSharper disable once AccessToDisposedClosure
                    progress.Report(new(value.Progress.Stage, updated.Sum(stream => stream.Captured),
                        updated.Sum(stream => stream.Encoded), value.Progress.CapturedFps,
                        updated.Max(stream => stream.Elapsed)));
                }
            });
            try
            {
                using var displays = new DesktopDisplayState(Screens);
                var results = await Task.Run(async () =>
                {
                    using var context =
                        new SceneCaptureContext(outputs.Max(output => output.Options.Fps), inputs: _inputs,
                            bufferWindows: outputs.Count > 1);
                    return await new MultiRecordingSession().RunAsync(outputs, output =>
                    {
                        var source = sources[output.Name];
                        return new SceneCapture(output.Options, displays.Read, source.Preset!, source.Runtime, context);
                    }, sink, stop, cancel, context.BeginFrame);
                }, CancellationToken.None);
                _streams =
                [
                    .. results.Select(result => new PresetRecordingStreamStatus(result.Name,
                        result.Success ? "complete" : "failed", result.Captured, result.Encoded, result.ElapsedSeconds,
                        result.Path, result.Error)),
                ];
                var successfulOutputs = results.Where(result => result.Success).Select(result => result.Path).ToArray();
                if (successfulOutputs.Length == 0)
                    throw new InvalidOperationException(string.Join(Environment.NewLine,
                        results.Select(result => result.Name + ": " + result.Error)));
                LastOutputs = successfulOutputs;
                LastPath = successfulOutputs[0];
                var copied = results.Where(result => result.Success &&
                                                     (sources[result.Name].Settings?.Copy ??
                                                      sources[result.Name].Preset?.Settings.Copy ?? true))
                    .Select(result => result.Path).ToArray();
                if (copied.Length > 0)
                    try
                    {
                        await CopyFilesAsync(copied);
                    }
                    catch (Exception error)
                    {
                        SetStatus(UiText.Get("ClipboardFailed") + error.Message);
                    }

                if (View is { } view && LastPath is { } path)
                    try
                    {
                        await view.LoadPreviewAsync(path);
                    }
                    catch (Exception error)
                    {
                        SetStatus(UiText.Get("PreviewFailed") + error.Message);
                    }

                SetStatus(results.All(result => result.Success)
                    ? UiText.Get("RecordingComplete")
                    : UiText.Get("RecordingFailed") + string.Join(", ",
                        results.Where(result => !result.Success).Select(result => result.Name)));
                _presets.Signal("saved");
                return new();
            }
            catch (OperationCanceledException)
            {
                _streams = [.. _streams.Select(stream => stream with { State = "cancelled" })];
                SetStatusKey("Cancelled");
                _presets.Signal("cancelled");
                return new(true);
            }
            catch (Exception error)
            {
                _streams =
                [
                    .. _streams.Select(stream =>
                        stream.State == "complete" ? stream : stream with { State = "failed", Error = error.Message }),
                ];
                SetStatus(UiText.Get("RecordingFailed") + error.Message);
                _presets.Signal("error");
                return new(Error: error);
            }
            finally
            {
                finished = true;
                _recordClock.Stop();
                IsRecording = IsBusy = false;
                _stop?.Dispose();
                _abort?.Dispose();
                _stop = _abort = null;
                if (!IsClosing && View is { } view) await view.ResumePreviewAsync();
                RefreshDisplaySelection();
                Notify();
            }
        }
    }
}

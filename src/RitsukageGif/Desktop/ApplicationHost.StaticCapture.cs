using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost : IStaticCaptureService
    {
        private Dictionary<string, PresetRecordingOutput> _captureDefinitions = new();

        async Task<MediaInfo> IStaticCaptureService.CaptureAsync(string output, StaticCaptureOptions options,
            CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(options.Image);
            var (plan, toneMap) = await Dispatcher.UIThread.InvokeAsync(() =>
                (PrepareStaticCapture(options, token), options.ToneMap ?? Settings.CaptureToneMap));
            await PrepareStaticSourceAsync(plan, token).ConfigureAwait(false);
            return await Task.Run(async () =>
            {
                var frames = CaptureStaticFrames(plan, false, toneMap, token);
                var pixels = CapturedFramePixels.ToStraightBgra(frames.Sdr, token);
                return await new StaticImageService().SavePixelsAsync(pixels, frames.Sdr.Width, frames.Sdr.Height,
                    output, options.Image, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        private StaticCapturePlan PrepareStaticCapture(StaticCaptureOptions options, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (IsClosing || IsSelecting || IsConfiguring)
                throw new InvalidOperationException(UiText.Get("CommandNotReady"));
            if (options.ScalePercent is < 1 or > 100)
                throw new ArgumentOutOfRangeException(nameof(options));
            var preset = _presets.ActivePreset ?? _manualPreset;
            var runtime = _presets.Runtime;
            var effective = preset?.Settings ?? new PresetRecordingSettings();
            if (options.OutputName is { Length: > 0 } name)
            {
                if (!_captureDefinitions.TryGetValue(name, out var definition))
                    throw new ArgumentException(UiText.Get("StaticCaptureOutputMissing"), nameof(options));
                preset = definition.Preset;
                runtime = definition.Runtime;
                effective = (preset?.Settings ?? new PresetRecordingSettings())
                    .Overlay(definition.Settings ?? new PresetRecordingSettings());
            }

            if (runtime is not null)
            {
                var snapshot = runtime.Snapshot;
                var frozen = new PresetRuntime();
                frozen.SetTime(runtime.Now);
                frozen.Publish(snapshot.Variables, snapshot.Layers);
                runtime = frozen;
            }

            var region = Region ?? default;
            if (preset?.Layout is { } layout)
            {
                region = new(0, 0, layout.Width, layout.Height);
            }
            else if (preset is not null)
            {
                var target = preset.Absolute ? null : WindowLocator.Find(preset, WindowLocator.List());
                if (!preset.Absolute && target is null)
                    throw new InvalidOperationException(UiText.Get("WindowNotFound"));
                region = preset.Resolve(target?.Bounds ?? default, target?.Client ?? default,
                    runtime?.Snapshot.Variables);
            }

            var recording = new RecordingOptions(region, Divisor: options.ScalePercent.HasValue
                    ? 1
                    : effective.Scale ?? Settings.Divisor,
                Cursor: options.IncludeCursor ?? effective.Cursor ?? Settings.Cursor, Transparent: true);
            recording.Validate();
            var scale = options.ScalePercent ?? 100;
            if (recording.Width * (scale / 100d) * recording.Height * (scale / 100d) > 64 * 1024 * 1024)
                throw new ArgumentException(UiText.Get("RecordingTooLarge"));
            var monitors = Screens.All.Select(screen => new PixelRect(screen.Bounds.X, screen.Bounds.Y,
                screen.Bounds.Width, screen.Bounds.Height)).ToArray();
            return new(recording, preset, runtime, monitors, scale);
        }
    }
}

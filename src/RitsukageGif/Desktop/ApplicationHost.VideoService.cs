using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost : IVideoRecordingService
    {
        private VideoCaptureHandle? _scriptVideoHandle;
        private Task<RecordingOutcome>? _scriptVideoOperation;

        async Task<VideoCaptureCapabilities> IVideoRecordingService.GetCapabilitiesAsync(CancellationToken token)
        {
            var capabilities = await VideoRecordingCapabilities.ProbeAsync(token);
            return new(capabilities.Executable is not null, capabilities.Mp4, capabilities.WebM,
                capabilities.Mp4Audio, capabilities.WebMAudio, OperatingSystem.IsWindows(),
                [.. capabilities.AudioInputFormats.Order(StringComparer.Ordinal)], capabilities.Mp4Hdr,
                capabilities.WebMHdr);
        }

        async Task<VideoCaptureHandle> IVideoRecordingService.StartAsync(string output, VideoCaptureOptions options,
            CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(options.AudioInputs);
            ArgumentException.ThrowIfNullOrWhiteSpace(output);
            var path = Path.GetFullPath(output);
            if (!options.Overwrite && File.Exists(path)) throw new IOException(UiText.Get("StaticCaptureOutputExists"));
            if (!Enum.TryParse<VideoContainer>(options.Format, true, out var container) || !Enum.IsDefined(container))
                throw new ArgumentException(UiText.Get("ScriptTypeError"));
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                token.ThrowIfCancellationRequested();
                var region = Region ?? throw new InvalidOperationException(UiText.Get("VideoNoRegion"));
                var settings = (_presets.ActivePreset ?? _manualPreset)?.Settings;
                var request = new VideoRecordingOptions(new(region, options.Fps ?? settings?.Fps ?? Settings.Fps,
                    options.Divisor ?? settings?.Scale ?? Settings.Divisor,
                    options.IncludeCursor ?? settings?.Cursor ?? Settings.Cursor))
                {
                    ColorMode = options.ColorMode ?? Settings.CaptureColorMode,
                    ToneMap = options.ToneMap ?? Settings.CaptureToneMap,
                    Container = container, Quality = options.Quality, Overwrite = options.Overwrite,
                    AudioInputs =
                        [.. options.AudioInputs.Select(input => new VideoAudioInput(input.Input, input.Format))],
                };
                request.Validate();
                var operation = StartVideoRecordingAsync(request, path, options.SystemAudio, options.SystemDevice,
                    options.Microphone, options.MicrophoneDevice);
                var handle = new VideoCaptureHandle(Guid.NewGuid().ToString("N"), path,
                    Outputs: request.GetOutputPaths(path));
                _scriptVideoHandle = handle;
                _scriptVideoOperation = operation;
                var cancellation = token.Register(() => Dispatcher.UIThread.Post(() =>
                {
                    if (!operation.IsCompleted && ReferenceEquals(_operation, operation)) CancelRecording();
                }));
                _ = ReleaseScriptVideoCancellationAsync(operation, cancellation);
                return handle;
            });
        }

        async Task<VideoCaptureHandle> IVideoRecordingService.StopAsync(string id, bool cancel, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var (handle, operation) = await Dispatcher.UIThread.InvokeAsync(() => GetScriptVideo(id));
            await Dispatcher.UIThread.InvokeAsync(() => StopOwnedVideoAsync(operation, cancel));
            return await CompleteScriptVideoAsync(handle, operation, token);
        }

        async Task<VideoCaptureHandle> IVideoRecordingService.WaitAsync(string id, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var (handle, operation) = await Dispatcher.UIThread.InvokeAsync(() => GetScriptVideo(id));
            return await CompleteScriptVideoAsync(handle, operation, token);
        }

        private (VideoCaptureHandle Handle, Task<RecordingOutcome> Operation) GetScriptVideo(string id)
        {
            if (_scriptVideoHandle is not { } handle || handle.Id != id || _scriptVideoOperation is not { } operation)
                throw new ArgumentException(UiText.Get("VideoHandleInvalid"), nameof(id));
            return (handle, operation);
        }

        private static async Task<VideoCaptureHandle> CompleteScriptVideoAsync(VideoCaptureHandle handle,
            Task<RecordingOutcome> operation, CancellationToken token)
        {
            var outcome = await operation.WaitAsync(token);
            if (outcome.Error is not null) throw new InvalidOperationException(outcome.Error.Message, outcome.Error);
            return handle with { Cancelled = outcome.Cancelled };
        }

        private static async Task ReleaseScriptVideoCancellationAsync(Task<RecordingOutcome> operation,
            CancellationTokenRegistration cancellation)
        {
            try
            {
                await operation;
            }
            finally
            {
                await cancellation.DisposeAsync();
            }
        }
    }
}

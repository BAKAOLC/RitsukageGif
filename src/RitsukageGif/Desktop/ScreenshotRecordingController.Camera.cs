namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotRecordingController
    {
        private readonly SemaphoreSlim _cameraGate = new(1, 1);
        private CancellationTokenSource? _cameraChange;
        private Task _cameraRefresh = Task.CompletedTask;
        private RecordingCameraSession? _cameraSession;
        private Exception? _cameraFailure;
        private long _cameraGeneration;

        internal RecordingCameraSession? CameraSession => Volatile.Read(ref _cameraSession);
        internal Func<RecordingCameraSession?, Task>? CameraSessionChanging { get; set; }

        internal void ReportCameraPreviewFailure(string message)
        {
            _cameraFailure = new InvalidOperationException(message);
            Error = message;
            Logger.ZLogError($"Recording camera preview failed. Detail={message}");
            Notify();
        }

        internal Task InitializeCameraDevicesAsync(IReadOnlyList<RecordingCameraDevice> devices)
        {
            _cameraDevices = devices;
            Camera = ScreenshotRecordingPreferenceAdapter.RestoreCamera(_preferences.Camera, devices);
            Notify();
            return RefreshCameraAsync();
        }

        internal Task ConfigureCameraAsync(RecordingCameraOptions options, bool devicesEdited = false)
        {
            Camera = options;
            var current = _host.Settings.ScreenshotRecording;
            _preferences = current with
            {
                Camera =
                ScreenshotRecordingPreferenceAdapter.CaptureCamera(options, current.Camera, devicesEdited),
            };
            _host.UpdateScreenshotRecordingPreferences(_preferences);
            Notify();
            return RefreshCameraAsync();
        }

        private Task RefreshCameraAsync()
        {
            _cameraChange?.Cancel();
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _cameraChange = cancellation;
            var generation = ++_cameraGeneration;
            _cameraRefresh = RefreshCameraCoreAsync(generation, cancellation);
            return _cameraRefresh;
        }

        private async Task RefreshCameraCoreAsync(long generation, CancellationTokenSource cancellation)
        {
            var entered = false;
            var token = cancellation.Token;
            RecordingCameraSession? created = null;
            var attached = false;
            try
            {
                await _cameraGate.WaitAsync(token);
                entered = true;
                if (generation != _cameraGeneration || IsClosed || Reviewing) return;
                var options = Camera;
                switch (_cameraSession)
                {
                    case { } current when SameDevices(current.Options, options):
                        current.ApplyOptions(options);
                        _cameraFailure = null;
                        return;
                    case { } configured:
                        await configured.ConfigureAsync(options, token);
                        _cameraFailure = null;
                        return;
                }

                created = await RecordingCameraSession.CreateAsync(options, token);
                token.ThrowIfCancellationRequested();
                if (generation != _cameraGeneration || IsClosed || Reviewing) return;
                if (CameraSessionChanging is { } preview)
                {
                    await preview(created);
                    attached = true;
                }

                token.ThrowIfCancellationRequested();
                if (generation != _cameraGeneration || IsClosed || Reviewing)
                {
                    if (CameraSessionChanging is { } detach) await detach(null);
                    attached = false;
                    return;
                }

                Volatile.Write(ref _cameraSession, created);
                created = null;
                _cameraFailure = null;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                _cameraFailure = error;
                Error = ApplicationHost.VideoErrorMessage(error);
                Logger.ZLogError(error, $"Recording camera configuration failed.");
            }
            finally
            {
                try
                {
                    if (created is not null)
                        try
                        {
                            if (attached && CameraSessionChanging is { } detach) await detach(null);
                        }
                        finally
                        {
                            await created.DisposeAsync();
                        }
                }
                finally
                {
                    if (entered) _cameraGate.Release();
                    if (ReferenceEquals(_cameraChange, cancellation)) _cameraChange = null;
                    cancellation.Dispose();
                    Notify();
                }
            }
        }

        internal async Task EnsureCameraReadyAsync()
        {
            if (IsClosed || Reviewing) return;
            if (CameraSession is null && _cameraRefresh.IsCompleted && _cameraFailure is null)
                await RefreshCameraAsync();
            else await _cameraRefresh;
            if (Camera.Enabled && _cameraFailure is { } failure) throw failure;
            if (CameraSession is null)
                throw _cameraFailure ?? new InvalidOperationException(UiText.Get("CommandNotReady"));
        }

        private async Task StopCameraAsync()
        {
            if (_cameraChange is { } cameraChange) await cameraChange.CancelAsync();
            try
            {
                try
                {
                    await _cameraRefresh;
                }
                finally
                {
                    await _cameraGate.WaitAsync(CancellationToken.None);
                    try
                    {
                        await ReleaseCameraSessionAsync();
                    }
                    finally
                    {
                        _cameraGate.Release();
                    }
                }
            }
            catch (Exception error)
            {
                _cameraFailure = error;
                Error = ApplicationHost.VideoErrorMessage(error);
                Logger.ZLogError(error, $"Recording camera cleanup failed.");
            }
        }

        private async Task ReleaseCameraSessionAsync()
        {
            var previous = Interlocked.Exchange(ref _cameraSession, null);
            try
            {
                if (CameraSessionChanging is { } preview) await preview(null);
            }
            finally
            {
                if (previous is not null) await previous.DisposeAsync();
            }
        }

        private static bool SameDevices(RecordingCameraOptions left, RecordingCameraOptions right)
        {
            return left.Enabled == right.Enabled && left.Primary.Device == right.Primary.Device &&
                   left.Secondary.Device == right.Secondary.Device;
        }
    }
}

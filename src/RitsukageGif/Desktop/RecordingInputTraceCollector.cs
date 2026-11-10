namespace RitsukageGif.Desktop
{
    internal sealed class RecordingInputTraceCollector : IAsyncDisposable
    {
        private static readonly ILogger Logger = LogHub.GetLogger("Desktop.Recording.InputTrace");
        private readonly RecordingControl _control;

        private readonly Func<RecordingFrameObservation, double, double, IReadOnlyList<RecordingInputGeometry>>
            _geometry;
        private readonly RecordingInputTraceWriter _writer;
        private readonly RecordingInputObserver _observer;
        private readonly RecordingCursorSampler _cursor;

        private readonly RecordingInputTrackState _inputTracks;
        private bool _subscribed;
        private bool _observerUnavailable;
        private bool _cursorUnavailable;
        private int _disposed;

        internal RecordingInputTraceCollector(string mediaPath, RecordingControl control,
            Func<RecordingFrameObservation, double, double, IReadOnlyList<RecordingInputGeometry>> geometry,
            bool recordKeyboard, bool recordMouse, Func<NativeCaptureCursorFrame?>? nativeCursorProvider = null,
            Func<string?>? nativeCursorReasonProvider = null, RecordingInputTrackState? inputTracks = null)
        {
            _control = control;
            _inputTracks = inputTracks ?? new(recordKeyboard, recordMouse);
            _geometry = geometry;
            _writer = new(mediaPath, recordKeyboard, recordMouse);
            try
            {
                _cursor = new(nativeCursorProvider, nativeCursorReasonProvider);
                _observer = new(recordKeyboard, recordMouse, OnInput, () => !_control.IsPaused);
            }
            catch
            {
                _cursor?.Dispose();
                _writer.Dispose();
                throw;
            }
        }

        internal string? UnavailableReason { get; private set; }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            if (_subscribed)
            {
                _control.FrameAccepted -= OnFrame;
                _subscribed = false;
            }

            try
            {
                if (!_observerUnavailable) await _observer.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                _cursor.Dispose();
                _writer.Dispose();
                Logger.ZLogDebug($"Recording input trace stopped.");
            }
        }

        internal async Task StartAsync(CancellationToken token)
        {
            try
            {
                await _observer.StartAsync(token).ConfigureAwait(false);
            }
            catch (Exception error) when (error is PlatformNotSupportedException or UnauthorizedAccessException)
            {
                UnavailableReason = error.Message;
                _observerUnavailable = true;
                _inputTracks.SetUnavailable(error.Message);
                try
                {
                    await _observer.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception cleanupError) when (cleanupError is PlatformNotSupportedException
                                                         or UnauthorizedAccessException)
                {
                }

                _writer.SetUnavailableTracks(error.Message);
                Logger.ZLogWarning($"Recording input tracks unavailable. Reason={error.Message}");
            }

            CheckCursorCapability();
            _control.FrameAccepted += OnFrame;
            _subscribed = true;
            Logger.ZLogDebug($"Recording input trace started.");
        }

        internal RecordingInputFrameSnapshot CaptureSnapshot(RecordingFrameObservation capture,
            CancellationToken token)
        {
            var tracks = _inputTracks.At(capture.CaptureTimestamp);
            var cursor = _cursor.Capture(tracks.Mouse >= 0 && !_observerUnavailable, token);
            CheckCursorCapability();
            return new(cursor, _geometry(capture, cursor.X, cursor.Y), tracks.Keyboard, tracks.Mouse);
        }

        private void CheckCursorCapability()
        {
            if (_cursorUnavailable || _cursor.UnavailableReason is not { } reason) return;
            _cursorUnavailable = true;
            UnavailableReason = UnavailableReason is null ? reason : UnavailableReason + Environment.NewLine + reason;
            _writer.SetCursorUnavailable(reason);
            Logger.ZLogWarning($"Recording cursor track unavailable. Reason={reason}");
        }

        private void OnFrame(object? sender, RecordingFrameObservation observation)
        {
            if (!_observerUnavailable && (_observer.Completion.IsFaulted || _observer.Completion.IsCanceled))
                _observer.Completion.GetAwaiter().GetResult();
            if (observation.SourceMetadata is not RecordingInputFrameSnapshot snapshot)
                throw new InvalidOperationException("The accepted recording frame has no capture-time input snapshot.");
            _writer.AppendFrame(observation, snapshot.Geometry, snapshot.Cursor,
                snapshot.KeyboardStartTimestamp, snapshot.MouseStartTimestamp);
        }

        private void OnInput(RecordingInputEvent input)
        {
            var tracks = _inputTracks.At(input.Timestamp);
            var start = input.Kind is RecordingInputEventKind.KeyDown or RecordingInputEventKind.KeyUp
                or RecordingInputEventKind.Modifiers
                ? tracks.Keyboard
                : tracks.Mouse;
            if (start < 0 || input.Timestamp < start) return;
            if (_control.TryMapInputTimestamp(input.Timestamp, out var position))
                _writer.AppendEvent(input, position);
        }
    }
}

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private ScreenshotRecordingWindow? _screenshotRecorder;
        private bool _screenshotRecordingCountingDown;
        private bool _screenshotRecordingPreparing;
        private CancellationTokenSource? _taskScreenshotRecordingCancellation;
        private bool HasStoppableScreenshotRecording => _screenshotRecordingCountingDown || _screenshotRecordingActive;
        private bool CanStopActiveRecording => !IsClosing && (IsRecording || _screenshotRecordingCountingDown);
        private RecordingAnnotationLayer? ScreenshotRecordingAnnotations => _screenshotRecorder?.Annotations;
        private RecordingInputTrackState? ScreenshotRecordingInputTracks => _screenshotRecorder?.InputTracks;
        private RecordingCameraSession? ScreenshotRecordingCameraSession => _screenshotRecorder?.CameraSession;

        internal void UpdateScreenshotRecordingPreparation(bool preparing, bool countingDown)
        {
            if (_screenshotRecordingPreparing == preparing && _screenshotRecordingCountingDown == countingDown) return;
            _screenshotRecordingPreparing = preparing;
            _screenshotRecordingCountingDown = countingDown;
            Notify();
        }

        private Task StopActiveRecordingAsync()
        {
            return HasStoppableScreenshotRecording && _screenshotRecorder is { } recorder
                ? recorder.StopRecordingAsync()
                : IsRecording
                    ? ToggleRecordingAsync()
                    : Task.CompletedTask;
        }

        internal void UpdateScreenshotRecordingPreferences(ScreenshotRecordingPreferences preferences)
        {
            var normalized = preferences.Normalize();
            if (IsClosing || Settings.ScreenshotRecording == normalized) return;
            Settings = Settings with { ScreenshotRecording = normalized };
            QueueSettingsSave(Settings);
        }

        private async Task OpenScreenshotRecorderAsync(SelectionResult selection, ScreenshotInvocation invocation)
        {
            var waitForRecorder = IsTaskHost && !TaskKeepAlive;
            if (IsClosing || IsSelecting || IsBusy || IsConfiguring ||
                _screenshotRecorder is { CanAcceptSelection: false })
            {
                if (waitForRecorder && invocation.PropagateErrors)
                    throw new InvalidOperationException(UiText.Get("CommandNotReady"));
                return;
            }

            if (waitForRecorder) invocation.CommandToken.ThrowIfCancellationRequested();
            ValidateRegion(selection.Region, !DesktopCapture.UsesSourceSelection && !selection.WindowSource,
                ScreenshotRecordingController.Divisor);
            HostLogger.ZLogInformation(
                $"Screenshot transferred to recording preparation. Region={selection.Region} WindowSource={selection.WindowSource} Cursor={selection.Cursor}");
            var recorder = _screenshotRecorder;
            if (recorder is null)
            {
                recorder = new(this, selection, () => Settings.Annotation, PersistAnnotationPreferences);
                _screenshotRecorder = recorder;
                recorder.Closed += (_, _) => _screenshotRecorder = null;
            }
            else
            {
                recorder.UpdateSelection(selection);
            }

            if (waitForRecorder) await WaitForTaskScreenshotRecorderAsync(recorder, invocation);
            else WindowActivation.Show(recorder);
        }

        private async Task WaitForTaskScreenshotRecorderAsync(ScreenshotRecordingWindow recorder,
            ScreenshotInvocation invocation)
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(invocation.CommandToken,
                _hostCancellation.Token);
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _taskScreenshotRecordingCancellation = cancellation;
            recorder.Closed += RecorderClosed;
            recorder.PublishOutput = invocation.PublishRecording;
            try
            {
                WindowActivation.Show(recorder);
                try
                {
                    await closed.Task.WaitAsync(cancellation.Token);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    await recorder.CloseForShutdownAsync();
                    await closed.Task;
                    throw;
                }

                cancellation.Token.ThrowIfCancellationRequested();
                if (!invocation.RecordingPublished)
                    throw new OperationCanceledException(UiText.Get("Cancelled"), new CancellationToken(true));
            }
            finally
            {
                _taskScreenshotRecordingCancellation = null;
                recorder.PublishOutput = null;
                recorder.Closed -= RecorderClosed;
            }

            return;

            void RecorderClosed(object? sender, EventArgs args)
            {
                closed.TrySetResult();
            }
        }

        private Task CloseScreenshotRecorderForShutdownAsync()
        {
            return _screenshotRecorder?.CloseForShutdownAsync() ?? Task.CompletedTask;
        }
    }
}

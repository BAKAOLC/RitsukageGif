using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private readonly RecordingRetentionService _retention = new(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RitsukageGif",
            "recordings.json"));

        private DispatcherTimer? _cleanupTimer;
        private CancellationTokenSource _cleanupCancellation = new();
        private Task _cleanupTask = Task.CompletedTask;

        private void ResetCleanupCancellation()
        {
            _cleanupCancellation.Cancel();
            _cleanupCancellation.Dispose();
            _cleanupCancellation = new CancellationTokenSource();
        }

        private void StartCleanup()
        {
            if (!_startServices || _cleanupTimer is not null) return;
            _cleanupTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(15) };
            _cleanupTimer.Tick += (_, _) =>
            {
                if (!_busy && !_configuring && _cleanupTask.IsCompleted) QueueCleanup();
            };
            _cleanupTimer.Start();
            QueueCleanup();
        }

        private void QueueCleanup(IReadOnlyList<string>? newFiles = null, IReadOnlyList<string>? clipboardPaths = null)
        {
            if (!_startServices || _closing) return;
            var policy = new RecordingRetentionPolicy(_settings.CleanupEnabled, _settings.CleanupMaxAgeDays,
                _settings.CleanupMaxFiles, _settings.CleanupMaxSizeMiB);
            if (newFiles is null && clipboardPaths is null && !policy.HasRules) return;
            // Clipboard ownership is updated before preview loading finishes; defer deletion until it has switched.
            if (_busy && newFiles is null) policy = policy with { Enabled = false };
            var previous = _cleanupTask;
            var protect = _lastOutputs.ToArray();
            _cleanupTask = RunCleanupAsync(previous, policy, newFiles?.ToArray() ?? [], protect,
                clipboardPaths?.ToArray(), _cleanupCancellation.Token);
        }

        private async Task RunCleanupAsync(Task previous, RecordingRetentionPolicy policy,
            IReadOnlyList<string> newFiles, IReadOnlyList<string> protect, IReadOnlyList<string>? clipboardPaths,
            CancellationToken token)
        {
            try
            {
                await previous;
                try
                {
                    await _retention.ApplyAsync(policy, newFiles, protect, clipboardPaths, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    // Keep ownership metadata even when disabling cleanup cancels pending deletion.
                    await _retention.ApplyAsync(new RecordingRetentionPolicy(), newFiles, protect, clipboardPaths);
                }
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or
                                              System.Text.Json.JsonException or ArgumentException)
            {
                System.Diagnostics.Trace.TraceError("Recording cleanup failed: {0}", error.Message);
            }
        }
    }
}

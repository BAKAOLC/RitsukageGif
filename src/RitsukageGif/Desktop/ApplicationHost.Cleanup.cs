using System.Text.Json;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private static readonly ILogger CleanupLogger = LogHub.GetLogger("Desktop.Retention");

        private readonly RecordingRetentionService _retention = new(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RitsukageGif",
            "recordings.json"));

        private CancellationTokenSource _cleanupCancellation = new();
        private Task _cleanupTask = Task.CompletedTask;

        private DispatcherTimer? _cleanupTimer;

        private void ResetCleanupCancellation()
        {
            _cleanupCancellation.Cancel();
            _cleanupCancellation.Dispose();
            _cleanupCancellation = new();
        }

        private void StartCleanup()
        {
            if (!_startServices || _cleanupTimer is not null) return;
            _cleanupTimer = new() { Interval = TimeSpan.FromMinutes(15) };
            _cleanupTimer.Tick += (_, _) =>
            {
                if (!IsBusy && !IsConfiguring && _cleanupTask.IsCompleted) QueueCleanup();
            };
            _cleanupTimer.Start();
            QueueCleanup();
        }

        private void QueueCleanup(IReadOnlyList<string>? newFiles = null, IReadOnlyList<string>? clipboardPaths = null)
        {
            if (!_startServices || IsClosing) return;
            var policy = new RecordingRetentionPolicy(Settings.CleanupEnabled, Settings.CleanupMaxAgeDays,
                Settings.CleanupMaxFiles, Settings.CleanupMaxSizeMiB);
            if (newFiles is null && clipboardPaths is null && !policy.HasRules) return;
            // Clipboard ownership is updated before preview loading finishes; defer deletion until it has switched.
            if (IsBusy && newFiles is null) policy = policy with { Enabled = false };
            var previous = _cleanupTask;
            var protect = LastOutputs.ToArray();
            _cleanupTask = RunCleanupAsync(previous, policy, newFiles?.ToArray() ?? [], protect,
                clipboardPaths?.ToArray(), _cleanupCancellation.Token);
        }

        private async Task RunCleanupAsync(Task previous, RecordingRetentionPolicy policy,
            string[] newFiles, string[] protect, IReadOnlyList<string>? clipboardPaths,
            CancellationToken token)
        {
            try
            {
                await previous;
                try
                {
                    CleanupLogger.ZLogDebug(
                        $"Recording retention pass started. NewFileCount={newFiles.Length} ProtectedFileCount={protect.Length}");
                    await _retention.ApplyAsync(policy, newFiles, protect, clipboardPaths, token);
                    CleanupLogger.ZLogInformation(
                        $"Recording retention pass completed. NewFileCount={newFiles.Length} ProtectedFileCount={protect.Length}");
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    // Keep ownership metadata even when disabling cleanup cancels pending deletion.
                    await _retention.ApplyAsync(new(), newFiles, protect, clipboardPaths, CancellationToken.None);
                    CleanupLogger.ZLogDebug(
                        $"Recording retention deletion cancelled; ownership metadata preserved. NewFileCount={newFiles.Length}");
                }
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or
                                              JsonException or ArgumentException)
            {
                CleanupLogger.ZLogError(error, $"Recording retention pass failed.");
            }
        }
    }
}

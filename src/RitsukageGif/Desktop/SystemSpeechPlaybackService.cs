using System.Diagnostics;
using System.Text.Json;

namespace RitsukageGif.Desktop
{
    internal sealed class SystemSpeechPlaybackService(Func<ProcessStartInfo>? startInfoFactory = null)
        : ISpeechPlaybackService
    {
        private static readonly Lock Gate = new();
        private static SpeechPlaybackRun? _active;
        private static readonly ILogger Logger = LogHub.GetLogger("Desktop.Speech");
        private bool _disposed;
        private SpeechPlaybackRun? _owned;

        public async Task<SpeechCapabilities> GetCapabilitiesAsync(CancellationToken token = default)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            try
            {
                var result = await SpeechProcess.RequestAsync("capabilities", null, timeout.Token, startInfoFactory)
                    .ConfigureAwait(false);
                return JsonSerializer.Deserialize(result, SpeechJsonContext.Default.SpeechCapabilities) ??
                       throw new InvalidDataException("The speech worker returned no capabilities.");
            }
            catch (OperationCanceledException)when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error)
            {
                Logger.ZLogWarning(error, $"System speech capability query failed.");
                return new(false, [], Reason: SpeechUnavailableReason.EngineError);
            }
        }

        public Task SpeakAsync(string text, string? language = null, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(text);
            if (text.Length > 200000) throw new ArgumentOutOfRangeException(nameof(text));
            if (string.IsNullOrWhiteSpace(text)) return Task.CompletedTask;
            lock (Gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                token.ThrowIfCancellationRequested();
                _active?.Cancellation.Cancel();
                var previous = _active?.Completion ?? Task.CompletedTask;
                var current = new SpeechPlaybackRun(token);
                _owned = _active = current;
                return current.Completion = Task.Run(async () =>
                {
                    try
                    {
                        try
                        {
                            await previous.ConfigureAwait(false);
                        }
                        catch (Exception)
                        {
                            /* Previous playback is observed by its caller. */
                        }

                        current.Cancellation.Token.ThrowIfCancellationRequested();
                        var result = await SpeechProcess
                            .RequestAsync("speak", new(text, language), current.Cancellation.Token, startInfoFactory)
                            .ConfigureAwait(false);
                        if (result.Trim() != "completed")
                            throw new InvalidDataException("The speech worker returned an invalid response.");
                    }
                    finally
                    {
                        lock (Gate)
                        {
                            if (ReferenceEquals(_active, current)) _active = null;
                            if (ReferenceEquals(_owned, current)) _owned = null;
                            current.Cancellation.Dispose();
                        }
                    }
                }, CancellationToken.None);
            }
        }

        public async Task StopAsync()
        {
            Task pending;
            lock (Gate)
            {
                _owned?.Cancellation.Cancel();
                pending = _owned?.Completion ?? Task.CompletedTask;
            }

            try
            {
                await pending.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception error)
            {
                Logger.ZLogDebug(error, $"A previous speech failure was observed during stop.");
            }
        }

        public async ValueTask DisposeAsync()
        {
            lock (Gate) _disposed = true;
            await StopAsync().ConfigureAwait(false);
        }
    }
}

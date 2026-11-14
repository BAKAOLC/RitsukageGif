using System.Runtime.ExceptionServices;

namespace RitsukageGif.Desktop
{
    internal sealed class ScreenshotRecordingAudioDiscovery(
        Func<bool, CancellationToken, IReadOnlyList<AudioCaptureDevice>> enumerate,
        CancellationToken token,
        TimeProvider? timeProvider = null)
    {
        private static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(2);
        private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
        private Task<Snapshot>? _current;

        public Task<Snapshot> ReadAsync(bool refresh = false)
        {
            if (_current is null || (_current.IsCompleted &&
                                     (refresh || !_current.IsCompletedSuccessfully ||
                                      _clock.GetElapsedTime(_current.Result.SampledAt) >= MaxAge)))
                _current = Task.Run(() =>
                {
                    var system = Probe(true);
                    var microphone = Probe(false);
                    token.ThrowIfCancellationRequested();
                    return new Snapshot(system, microphone, _clock.GetTimestamp());
                }, token);

            return _current;
        }

        private Endpoint Probe(bool system)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                return new(enumerate(system, token).Any(device => device is { IsDefault: true, Available: true }),
                    null);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                return new(false, error);
            }
        }

        public readonly record struct Snapshot(Endpoint System, Endpoint Microphone, long SampledAt)
        {
            public (bool System, bool Microphone) Select(bool system, bool microphone)
            {
                return (System.Select(system), Microphone.Select(microphone));
            }
        }

        public readonly record struct Endpoint(bool Available, Exception? Error)
        {
            public bool Select(bool requested)
            {
                if (!requested) return false;
                if (Error is not null) ExceptionDispatchInfo.Throw(Error);
                return Available;
            }
        }
    }
}

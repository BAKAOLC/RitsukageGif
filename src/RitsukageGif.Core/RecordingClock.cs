using System.Diagnostics;

namespace RitsukageGif.Core
{
    internal sealed class RecordingClock
    {
        private readonly RecordingControl _control;
        private long _startedAt = -1;

        public RecordingClock(RecordingControl control, bool start = true)
        {
            _control = control;
            if (start) Start();
        }

        public long RawStartedAt { get; private set; }
        public long Origin => Interlocked.Read(ref _startedAt);

        public long ElapsedTicks => Interlocked.Read(ref _startedAt) is var start && start >= 0
            ? _control.GetTimestamp() - start
            : 0;

        public TimeSpan Elapsed => TimeSpan.FromSeconds(ElapsedTicks / (double)Stopwatch.Frequency);
        public long ElapsedMilliseconds => (long)(ElapsedTicks * 1000.0 / Stopwatch.Frequency);

        public void Start()
        {
            var origin = _control.GetTimestamp(out var raw);
            RawStartedAt = raw;
            Interlocked.CompareExchange(ref _startedAt, origin, -1);
        }

        public async ValueTask WaitUntilAsync(double milliseconds, CancellationToken cancellationToken)
        {
            while (true)
            {
                await _control.WaitWhilePausedAsync(cancellationToken);
                var remaining = milliseconds - Elapsed.TotalMilliseconds;
                if (remaining <= 0) return;
                await Task.Delay(TimeSpan.FromMilliseconds(remaining), cancellationToken);
            }
        }
    }
}

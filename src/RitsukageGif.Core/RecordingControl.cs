using System.Diagnostics;

namespace RitsukageGif.Core
{
    public sealed partial class RecordingControl
    {
        private readonly Lock _syncRoot = new();
        private readonly List<(long Start, long End)> _pauses = [];
        private PauseState _state = new(0, 0, null, 0);
        private bool _canPause = true;

        public bool IsPaused => Volatile.Read(ref _state).Resume is not null;
        public bool CanPause => Volatile.Read(ref _canPause);

        internal long Generation => Volatile.Read(ref _state).Generation;

        public event EventHandler? StateChanged;
        public event EventHandler<RecordingFrameObservation>? FrameAccepted;

        internal void NotifyFrameAccepted(RecordingFrameObservation observation)
        {
            lock (_syncRoot)
            {
                var timeline = Timeline(observation.StreamName);
                if (observation.CaptureSequence != timeline.LastSequence)
                {
                    timeline.LastSequence = observation.CaptureSequence;
                    timeline.AcceptedCaptures++;
                }
            }

            FrameAccepted?.Invoke(this, observation);
        }

        // ReSharper disable once UnusedMethodReturnValue.Global
        public bool Pause()
        {
            lock (_syncRoot)
            {
                var state = _state;
                if (!_canPause || state.Resume is not null) return false;
                Volatile.Write(ref _state, new(Stopwatch.GetTimestamp(), state.PausedTicks,
                    new(TaskCreationOptions.RunContinuationsAsynchronously), state.Generation + 1));
            }

            StateChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public bool Resume()
        {
            TaskCompletionSource resumed;
            lock (_syncRoot)
            {
                var state = _state;
                if (state.Resume is null) return false;
                var now = Stopwatch.GetTimestamp();
                _pauses.Add((state.StartedAt, now));
                resumed = state.Resume;
                Volatile.Write(ref _state, new(0, state.PausedTicks + now - state.StartedAt,
                    null, state.Generation));
            }

            resumed.SetResult();
            StateChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        internal long GetTimestamp()
        {
            return GetTimestamp(out _);
        }

        internal long GetTimestamp(out long rawTimestamp)
        {
            lock (_syncRoot)
            {
                rawTimestamp = Stopwatch.GetTimestamp();
                var state = _state;
                return (state.Resume is null ? rawTimestamp : state.StartedAt) - state.PausedTicks;
            }
        }

        internal void SetCanPause(bool canPause)
        {
            lock (_syncRoot)
            {
                if (_canPause == canPause) return;
                Volatile.Write(ref _canPause, canPause);
            }

            if (!canPause && Resume()) return;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        private bool TryMapTimestamp(long timestamp, out long activeTimestamp)
        {
            lock (_syncRoot)
            {
                long paused = 0;
                foreach (var interval in _pauses)
                {
                    if (timestamp < interval.Start) break;
                    if (timestamp < interval.End)
                    {
                        activeTimestamp = 0;
                        return false;
                    }

                    paused += interval.End - interval.Start;
                }

                var state = _state;
                if (state.Resume is not null && timestamp >= state.StartedAt)
                {
                    activeTimestamp = 0;
                    return false;
                }

                activeTimestamp = timestamp - paused;
                return true;
            }
        }

        internal ValueTask WaitWhilePausedAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resumed = Volatile.Read(ref _state).Resume;
            return resumed is null ? ValueTask.CompletedTask : new(resumed.Task.WaitAsync(cancellationToken));
        }

        internal bool IsCurrentCapture(long generation)
        {
            var state = Volatile.Read(ref _state);
            return state.Resume is null && state.Generation == generation;
        }
    }
}

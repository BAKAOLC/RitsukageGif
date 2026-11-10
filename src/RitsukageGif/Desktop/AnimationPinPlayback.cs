using System.Diagnostics;

namespace RitsukageGif.Desktop
{
    internal sealed class AnimationPinPlayback
    {
        private readonly long[] _ends;
        private readonly double _frequency;
        private readonly int _plays;
        private readonly Func<long> _timestamp;
        private long _anchor;
        private double _position;

        internal AnimationPinPlayback(AnimationPinMetadata metadata, Func<long>? timestamp = null,
            long frequency = 0)
        {
            _timestamp = timestamp ?? Stopwatch.GetTimestamp;
            _frequency = frequency > 0 ? frequency : Stopwatch.Frequency;
            _plays = metadata.TotalPlays;
            _ends = new long[metadata.Frames.Count];
            long sum = 0;
            for (var i = 0; i < _ends.Length; i++)
                _ends[i] = sum = checked(sum + Math.Max(1, metadata.Frames[i].DurationTicks));
            if (sum <= 0)
                throw new ArgumentException("The animation must contain a positive duration.", nameof(metadata));
            _anchor = _timestamp();
        }

        public bool Playing { get; private set; }
        public double Speed { get; private set; } = 1;

        private long DurationTicks => _ends[^1];

        public int FrameIndex
        {
            get
            {
                var position = Position();
                if (Completed(position))
                {
                    Pause();
                    return _ends.Length - 1;
                }

                var local = position % DurationTicks;
                var index = Array.BinarySearch(_ends, (long)local + 1);
                return index >= 0 ? index : ~index;
            }
        }

        public TimeSpan NextDelay
        {
            get
            {
                var index = FrameIndex;
                var remaining = _ends[index] - Position() % DurationTicks;
                return TimeSpan.FromMilliseconds(Math.Clamp(remaining / TimeSpan.TicksPerMillisecond / Speed, 4, 250));
            }
        }

        public void Play()
        {
            if (Playing) return;
            if (Completed(_position)) _position = 0;
            _anchor = _timestamp();
            Playing = true;
        }

        public void Pause()
        {
            if (!Playing) return;
            _position = Position();
            Playing = false;
        }

        public void SetSpeed(double speed)
        {
            if (!double.IsFinite(speed) || speed is < 0.1 or > 8) throw new ArgumentOutOfRangeException(nameof(speed));
            _position = Position();
            _anchor = _timestamp();
            Speed = speed;
        }

        public void Step(int direction)
        {
            var index = FrameIndex;
            Pause();
            index = (index + direction + _ends.Length) % _ends.Length;
            _position = index == 0 ? 0 : _ends[index - 1];
        }

        private bool Completed(double position)
        {
            return _plays != 0 && position >= (double)DurationTicks * _plays;
        }

        private double Position()
        {
            return _position + (Playing
                ? Math.Max(0, _timestamp() - _anchor) / _frequency * TimeSpan.TicksPerSecond * Speed
                : 0);
        }
    }
}

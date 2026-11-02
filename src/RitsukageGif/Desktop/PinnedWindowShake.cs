namespace RitsukageGif.Desktop
{
    internal sealed class PinnedWindowShake
    {
        private readonly long[] _legs = new long[5];
        private int _direction;
        private double _distance;
        private int _legCount;
        private int _legIndex;
        private double _previous;
        private long _previousTime;
        private bool _recorded;
        private long _triggeredAt = long.MinValue;

        internal bool Observe(double x, long milliseconds)
        {
            if (_direction == 0 || milliseconds - _previousTime > 600)
            {
                _previous = x;
                _previousTime = milliseconds;
                _direction = 1;
                _distance = 0;
                _recorded = false;
                _legCount = 0;
                return false;
            }

            var delta = x - _previous;
            _previous = x;
            _previousTime = milliseconds;
            if (delta == 0) return false;
            var direction = delta > 0 ? 1 : -1;
            if (direction != _direction)
            {
                _direction = direction;
                _distance = 0;
                _recorded = false;
            }

            _distance += Math.Abs(delta);
            if (_recorded || _distance < 30) return false;
            _recorded = true;
            _legs[_legIndex] = milliseconds;
            _legIndex = (_legIndex + 1) % _legs.Length;
            _legCount = Math.Min(_legCount + 1, _legs.Length);
            if (_legCount != _legs.Length || milliseconds - _legs[_legIndex] > 600 ||
                (_triggeredAt != long.MinValue && milliseconds - _triggeredAt < 1000)) return false;
            _triggeredAt = milliseconds;
            _legCount = 0;
            return true;
        }
    }
}

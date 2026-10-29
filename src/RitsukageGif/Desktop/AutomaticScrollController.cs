namespace RitsukageGif.Desktop
{
    internal sealed class AutomaticScrollController(IAutomaticScrollInput input)
    {
        private byte[] _lastFrame = [];
        private long _lastInput;
        private int _offsetBeforeInput;
        private bool _pendingInput;
        private long _stableSince;
        private int _unchangedInputs;

        internal AutomaticScrollState State { get; private set; }

        internal void Configure(bool enabled, bool paused)
        {
            State = enabled
                ? paused ? AutomaticScrollState.Paused : AutomaticScrollState.Running
                : AutomaticScrollState.Manual;
            _lastFrame = [];
            _lastInput = _stableSince = 0;
            _pendingInput = false;
            _unchangedInputs = 0;
        }

        internal void Observe(ScrollCaptureResult result, ReadOnlySpan<byte> pixels, ScrollCaptureAxis axis,
            PixelRect region, long milliseconds, CancellationToken token)
        {
            if (State != AutomaticScrollState.Running) return;
            token.ThrowIfCancellationRequested();
            if (result.Status is ScrollCaptureStatus.InvalidPosition or ScrollCaptureStatus.LimitReached)
            {
                State = AutomaticScrollState.Unmatched;
                return;
            }

            var unchanged = pixels.SequenceEqual(_lastFrame);
            if (!unchanged)
            {
                if (_lastFrame.Length != pixels.Length) _lastFrame = new byte[pixels.Length];
                pixels.CopyTo(_lastFrame);
                _stableSince = milliseconds;
                return;
            }

            if (milliseconds - _stableSince < 150 || milliseconds - _lastInput < 250) return;
            if (result.Status is ScrollCaptureStatus.Unmatched or ScrollCaptureStatus.Ambiguous)
            {
                State = AutomaticScrollState.Unmatched;
                return;
            }

            if (_pendingInput)
            {
                if (result.Offset < _offsetBeforeInput)
                {
                    State = AutomaticScrollState.Paused;
                    return;
                }

                _unchangedInputs = result.Offset == _offsetBeforeInput ? _unchangedInputs + 1 : 0;
                _pendingInput = false;
                if (_unchangedInputs >= 4)
                {
                    State = AutomaticScrollState.NoChange;
                    return;
                }
            }

            var delivered = input.Scroll(axis, region, token);
            State = delivered switch
            {
                AutomaticScrollInputResult.Sent => AutomaticScrollState.Running,
                AutomaticScrollInputResult.AtEnd => AutomaticScrollState.AtEnd,
                AutomaticScrollInputResult.Unavailable => AutomaticScrollState.Unavailable,
                _ => AutomaticScrollState.Failed,
            };
            if (delivered != AutomaticScrollInputResult.Sent) return;
            _lastInput = milliseconds;
            _offsetBeforeInput = result.Offset;
            _pendingInput = true;
        }
    }
}

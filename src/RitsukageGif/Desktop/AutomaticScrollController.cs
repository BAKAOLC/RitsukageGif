namespace RitsukageGif.Desktop
{
    internal sealed class AutomaticScrollController(IAutomaticScrollInput input)
    {
        private bool _hasObservation;
        private long _lastInput;
        private bool _lastMatched;
        private int _lastOffset;
        private int _offsetBeforeInput;
        private bool _pendingInput;
        private bool _pointerOutside;
        private long _stableSince;
        private int _unchangedInputs;

        internal AutomaticScrollState State { get; private set; }

        internal bool WantsObservation => State == AutomaticScrollState.Running ||
                                          (State == AutomaticScrollState.Unmatched && _pointerOutside);

        internal void Configure(bool enabled, bool paused)
        {
            State = enabled
                ? paused ? AutomaticScrollState.Paused : AutomaticScrollState.Running
                : AutomaticScrollState.Manual;
            _pointerOutside = false;
            ResetViewport();
        }

        internal void ResetViewport()
        {
            _hasObservation = false;
            _lastInput = _stableSince = 0;
            _pendingInput = false;
            _unchangedInputs = 0;
        }

        internal void Observe(ScrollCaptureResult result, ScrollCaptureAxis axis,
            PixelRect region, long milliseconds, CancellationToken token)
        {
            if (!WantsObservation) return;
            token.ThrowIfCancellationRequested();
            if (result.Status is ScrollCaptureStatus.InvalidPosition or ScrollCaptureStatus.LimitReached)
            {
                _pointerOutside = false;
                State = AutomaticScrollState.Unmatched;
                return;
            }

            var matched = result.Status is ScrollCaptureStatus.Started or ScrollCaptureStatus.Appended or
                ScrollCaptureStatus.AlreadyCovered;
            if (!_hasObservation || matched != _lastMatched || (matched && result.Offset != _lastOffset))
            {
                _hasObservation = true;
                _lastMatched = matched;
                _lastOffset = result.Offset;
                _stableSince = milliseconds;
                return;
            }

            if (milliseconds - _stableSince < 150 || milliseconds - _lastInput < 250) return;
            if (!matched)
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
            _pointerOutside = delivered == AutomaticScrollInputResult.PointerOutside;
            State = delivered switch
            {
                AutomaticScrollInputResult.Sent or AutomaticScrollInputResult.PointerOutside =>
                    AutomaticScrollState.Running,
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

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotScrollingSession
    {
        private AutomaticScrollController? _automatic;
        private bool _automaticDragging;
        private bool _automaticEnabled;
        private bool _automaticHeld;
        private int _fileOnlyWarningLength;
        private bool _fileOnlyWarningResolved;

        // ReSharper disable once UnusedAutoPropertyAccessor.Global
        internal Func<PixelRect, CancellationToken, byte[]>? CaptureFrame { get; set; }

        internal bool AutomaticAvailable => _automatic is not null;

        internal AutomaticScrollState AutomaticState
        {
            get
            {
                lock (_sync) return _automatic?.State ?? AutomaticScrollState.Manual;
            }
        }

        internal bool AutomaticEnabled
        {
            get
            {
                lock (_sync) return _automaticEnabled;
            }
            set
            {
                lock (_sync)
                {
                    _automaticEnabled = value && _automatic is not null;
                    _automatic?.Configure(_automaticEnabled, Paused);
                    NotifyAutomaticState();
                }
            }
        }

        internal event Action<AutomaticScrollState>? AutomaticChanged;

        internal void ConfigureAutomaticInput(IAutomaticScrollInput? input)
        {
            lock (_sync)
            {
                _automatic = input is null ? null : new(input);
                _automaticEnabled = false;
            }
        }

        internal void SetAutomaticDragging(bool dragging)
        {
            lock (_sync) _automaticDragging = dragging;
        }

        internal void SetAutomaticHold(bool held)
        {
            lock (_sync)
            {
                _automaticHeld = held;
                _automatic?.ResetViewport();
            }
        }

        internal void ConfigureFileOnlyWarning(int length)
        {
            lock (_sync) _fileOnlyWarningLength = length;
        }

        internal void ResolveFileOnlyWarning(int generation)
        {
            lock (_sync)
            {
                if (generation != _generation) return;
                _fileOnlyWarningResolved = true;
            }
        }

        private void ObserveAutomatic(ScrollCaptureResult result, PixelRect region,
            CancellationToken token)
        {
            if (_automaticDragging || _automaticHeld || !_automaticEnabled || _automatic is null) return;
            var length = _axis == ScrollCaptureAxis.Vertical ? result.Height : result.Width;
            if (_fileOnlyWarningLength > 0 && length > _fileOnlyWarningLength &&
                !_fileOnlyWarningResolved) return;
            var previous = _automatic.State;
            _automatic.Observe(result, _axis, region, Environment.TickCount64, token);
            if (!_automatic.WantsObservation) _automaticEnabled = false;
            if (_automatic.State != previous) NotifyAutomaticState();
        }

        private void NotifyAutomaticState()
        {
            AutomaticChanged?.Invoke(_automatic?.State ?? AutomaticScrollState.Manual);
        }
    }
}

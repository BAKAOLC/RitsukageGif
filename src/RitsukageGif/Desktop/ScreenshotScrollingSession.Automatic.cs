namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotScrollingSession
    {
        private AutomaticScrollController? _automatic;
        private bool _automaticEnabled;

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

        private void ObserveAutomatic(ScrollCaptureResult result, ReadOnlySpan<byte> pixels, PixelRect region,
            CancellationToken token)
        {
            if (!_automaticEnabled || _automatic is null) return;
            var previous = _automatic.State;
            _automatic.Observe(result, pixels, _axis, region, Environment.TickCount64, token);
            if (_automatic.State != AutomaticScrollState.Running) _automaticEnabled = false;
            if (_automatic.State != previous) NotifyAutomaticState();
        }

        private void NotifyAutomaticState()
        {
            AutomaticChanged?.Invoke(_automatic?.State ?? AutomaticScrollState.Manual);
        }
    }
}

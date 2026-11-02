namespace RitsukageGif.Desktop
{
    internal sealed class TrayClickSequencer(
        Action<TrayMouseGesture> execute,
        Func<Action, TimeSpan, IDisposable> schedule,
        Func<TimeSpan> doubleClickTime) : IDisposable
    {
        private bool _disposed;
        private IDisposable? _pending;
        private bool _suppressNextUp;

        public void Dispose()
        {
            _disposed = true;
            _pending?.Dispose();
            _pending = null;
        }

        internal void Process(uint message, bool hasDoubleAction)
        {
            if (_disposed) return;
            switch (message)
            {
                case 0x202:
                    if (_suppressNextUp)
                    {
                        _suppressNextUp = false;
                        return;
                    }

                    ProcessGesture(TrayMouseGesture.SingleClick, hasDoubleAction);
                    break;
                case 0x203 when hasDoubleAction:
                    _suppressNextUp = true;
                    ProcessGesture(TrayMouseGesture.DoubleClick, hasDoubleAction);
                    break;
                case 0x208:
                    ProcessGesture(TrayMouseGesture.MiddleClick, hasDoubleAction);
                    break;
            }
        }

        internal void ProcessGesture(TrayMouseGesture gesture, bool hasDoubleAction)
        {
            if (_disposed) return;
            if (gesture == TrayMouseGesture.MiddleClick)
            {
                execute(gesture);
                return;
            }

            _pending?.Dispose();
            _pending = null;
            if (gesture == TrayMouseGesture.DoubleClick && hasDoubleAction)
            {
                execute(gesture);
                return;
            }

            if (!hasDoubleAction) execute(TrayMouseGesture.SingleClick);
            else
                _pending = schedule(() =>
                {
                    _pending = null;
                    if (!_disposed) execute(TrayMouseGesture.SingleClick);
                }, doubleClickTime());
        }
    }
}

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

                    _pending?.Dispose();
                    _pending = null;
                    if (hasDoubleAction)
                        _pending = schedule(() =>
                        {
                            _pending = null;
                            if (!_disposed) execute(TrayMouseGesture.SingleClick);
                        }, doubleClickTime());
                    else execute(TrayMouseGesture.SingleClick);
                    break;
                case 0x203 when hasDoubleAction:
                    _pending?.Dispose();
                    _pending = null;
                    _suppressNextUp = true;
                    execute(TrayMouseGesture.DoubleClick);
                    break;
                case 0x208:
                    execute(TrayMouseGesture.MiddleClick);
                    break;
            }
        }
    }
}

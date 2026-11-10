namespace RitsukageGif.Desktop
{
    internal sealed class TrayClickSequencer(
        Func<TrayMouseGesture, SystemTrayAction> resolve,
        Action<SystemTrayAction> execute,
        Func<Action, TimeSpan, IDisposable> schedule) : IDisposable
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

        internal void Process(uint message)
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

                    ProcessGesture(TrayMouseGesture.SingleClick);
                    break;
                case 0x203:
                    _suppressNextUp = true;
                    ProcessGesture(TrayMouseGesture.DoubleClick);
                    break;
                case 0x208:
                    ProcessGesture(TrayMouseGesture.MiddleClick);
                    break;
            }
        }

        internal void ProcessGesture(TrayMouseGesture gesture, bool supportsDoubleClick = true)
        {
            if (_disposed) return;
            var action = resolve(gesture);
            // ReSharper disable once ConvertIfStatementToSwitchStatement
            if (gesture == TrayMouseGesture.MiddleClick)
            {
                execute(action);
                return;
            }

            if (gesture == TrayMouseGesture.SingleClick && action == SystemTrayAction.None) return;

            _pending?.Dispose();
            _pending = null;
            if (gesture == TrayMouseGesture.DoubleClick || !supportsDoubleClick ||
                resolve(TrayMouseGesture.DoubleClick) == SystemTrayAction.None)
            {
                execute(action);
                return;
            }

            _pending = schedule(() =>
            {
                _pending = null;
                if (!_disposed) execute(action);
            }, TimeSpan.FromMilliseconds(100));
        }
    }
}

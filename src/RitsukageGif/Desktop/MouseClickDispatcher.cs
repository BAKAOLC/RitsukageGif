using Avalonia.Input;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed class MouseClickDispatcher : IDisposable
    {
        private readonly Queue<Action> _ready = new();
        private Func<bool>? _canExecute;
        private IDisposable? _clickTimer, _retryTimer;
        private Action? _pending;
        private InputMouseGesture _pendingGesture;

        public void Dispose()
        {
            CancelPending();
            _retryTimer?.Dispose();
            _retryTimer = null;
            _ready.Clear();
            _canExecute = null;
        }

        internal void Press(MouseButton button, KeyModifiers modifiers, int clickCount)
        {
            if (clickCount < 2 || modifiers != _pendingGesture.Modifiers) return;
            var kind = button switch
            {
                MouseButton.Left => MouseGestureKind.LeftClick,
                MouseButton.Right => MouseGestureKind.RightClick,
                MouseButton.Middle => MouseGestureKind.MiddleClick,
                MouseButton.XButton1 => MouseGestureKind.X1Click,
                MouseButton.XButton2 => MouseGestureKind.X2Click,
                _ => MouseGestureKind.None,
            };
            if (_pendingGesture.Kind == kind) CancelPending();
        }

        internal void Dispatch(MouseCommandBinding binding, IReadOnlyList<MouseCommandBinding> bindings,
            Action execute, TimeSpan doubleClickTime, Func<bool> canExecute)
        {
            _canExecute = canExecute;
            CommitPending();
            var doubleKind = binding.Gesture.Kind switch
            {
                MouseGestureKind.LeftClick => MouseGestureKind.LeftDoubleClick,
                MouseGestureKind.RightClick => MouseGestureKind.RightDoubleClick,
                MouseGestureKind.MiddleClick => MouseGestureKind.MiddleDoubleClick,
                MouseGestureKind.X1Click => MouseGestureKind.X1DoubleClick,
                MouseGestureKind.X2Click => MouseGestureKind.X2DoubleClick,
                _ => MouseGestureKind.None,
            };
            if (binding.Action != MouseAction.Select && doubleKind != MouseGestureKind.None &&
                MouseCommands.TryResolve(binding.Definition.Context, binding.Gesture with { Kind = doubleKind },
                    bindings,
                    out var doubleBinding) && doubleBinding!.Action != binding.Action)
            {
                _pending = execute;
                _pendingGesture = binding.Gesture;
                _clickTimer = DispatcherTimer.RunOnce(() =>
                {
                    CommitPending();
                    TryFlush();
                }, doubleClickTime);
            }
            else
            {
                _ready.Enqueue(execute);
            }

            TryFlush();
        }

        private void CancelPending()
        {
            _clickTimer?.Dispose();
            _clickTimer = null;
            _pending = null;
        }

        private void CommitPending()
        {
            if (_pending is { } execute) _ready.Enqueue(execute);
            CancelPending();
        }

        private void TryFlush()
        {
            _retryTimer?.Dispose();
            _retryTimer = null;
            while (_ready.Count > 0 && _canExecute?.Invoke() == true) _ready.Dequeue()();
            if (_ready.Count > 0) _retryTimer = DispatcherTimer.RunOnce(TryFlush, TimeSpan.FromMilliseconds(16));
        }
    }
}

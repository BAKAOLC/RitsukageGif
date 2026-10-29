using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed class ScopedCaptureInputSubscription : IDisposable
    {
        [ThreadStatic] private static ScopedCaptureInputSubscription? _current;
        private readonly CancellationToken _cancellation;
        private readonly Action _cancelled;
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Action<Exception> _failed;
        private readonly Lock _lifetime = new();

        private readonly TaskCompletionSource<IDisposable> _ready =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly Action _released;
        private readonly CancellationTokenSource _stop = new();
        private readonly Thread _thread;
        private int _abandoned;
        private CancellationTokenRegistration _cancellationRegistration;
        private bool _nativeFinished;
        private int _terminal;

        internal ScopedCaptureInputSubscription(Action released, Action cancelled, Action<Exception> failed,
            CancellationToken cancellation)
        {
            _released = released;
            _cancelled = cancelled;
            _failed = failed;
            _cancellation = cancellation;
            _thread = new(Run) { IsBackground = true, Name = "Capture input observation" };
        }

        internal Task<IDisposable> Ready => _ready.Task;
        internal Task Completion => _completion.Task;

        public void Dispose()
        {
            Interlocked.Exchange(ref _abandoned, 1);
            Stop();
        }

        internal void Start()
        {
            try
            {
                _cancellationRegistration = _cancellation.Register(static state =>
                    ((ScopedCaptureInputSubscription)state!).Dispose(), this);
                _thread.Start();
            }
            catch (Exception error)
            {
                _ready.TrySetException(error);
                FinishNativeLifetime();
            }
        }

        private void Stop()
        {
            lock (_lifetime)
                if (!_nativeFinished)
                    _stop.Cancel();
        }

        private unsafe void Run()
        {
            nint mouse = 0, keyboard = 0;
            _current = this;
            try
            {
                NativeMethods.PeekMessageW(out _, 0, 0, 0, 0);
                if (_stop.IsCancellationRequested) return;
                var module = NativeMethods.MouseHookModule(0);
                mouse = NativeMethods.SetMouseHook(14,
                    (nint)(delegate* unmanaged[Stdcall]<int, nuint, nint, nint>)&OnMouse, module, 0);
                if (mouse == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
                keyboard = NativeMethods.SetMouseHook(13,
                    (nint)(delegate* unmanaged[Stdcall]<int, nuint, nint, nint>)&OnKeyboard, module, 0);
                if (keyboard == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
                if (_stop.IsCancellationRequested) return;
                _ready.TrySetResult(this);
                // Cover input changes between pointer-down and hook installation without polling.
                if (NativeMethods.GetAsyncKeyState(0x1B) < 0) Signal(_cancelled);
                else if (NativeMethods.GetAsyncKeyState(1) >= 0) Signal(_released);
                var cancellation = _stop.Token.WaitHandle.SafeWaitHandle.DangerousGetHandle();
                while (!_stop.IsCancellationRequested)
                {
                    var result = NativeMethods.MsgWaitForMultipleObjectsEx(1, &cancellation, uint.MaxValue, 0x04FF, 4);
                    switch (result)
                    {
                        case 0: return;
                        case uint.MaxValue: throw new Win32Exception(Marshal.GetLastPInvokeError());
                    }

                    while (!_stop.IsCancellationRequested &&
                           NativeMethods.PeekMessageW(out var message, 0, 0, 0, 1) != 0)
                        if (message.Id == 0x0012)
                            throw new InvalidOperationException("Capture input observation ended unexpectedly.");
                }
            }
            catch (Exception error)
            {
                if (_ready.Task.IsCompletedSuccessfully) Signal(() => _failed(error));
                else _ready.TrySetException(error);
            }
            finally
            {
                RemoveHook(keyboard);
                RemoveHook(mouse);
                _current = null;
                _ready.TrySetCanceled(_cancellation);
                FinishNativeLifetime();
            }
        }

        private void RemoveHook(nint hook)
        {
            if (hook == 0 || NativeMethods.RemoveMouseHook(hook) != 0) return;
            var error = new Win32Exception(Marshal.GetLastPInvokeError());
            TraceFailure("Could not remove the capture input hook: {0}", error);
            if (_ready.Task.IsCompletedSuccessfully) Signal(() => _failed(error));
        }

        private void FinishNativeLifetime()
        {
            _cancellationRegistration.Dispose();
            lock (_lifetime)
            {
                _nativeFinished = true;
                _stop.Dispose();
            }

            _completion.TrySetResult();
        }

        private void Signal(Action callback)
        {
            if (Interlocked.CompareExchange(ref _terminal, 1, 0) != 0) return;
            Stop();
            try
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (Volatile.Read(ref _abandoned) == 0 && !_cancellation.IsCancellationRequested) callback();
                });
            }
            catch (Exception error)
            {
                TraceFailure("Could not dispatch capture input observation: {0}", error);
            }
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static nint OnMouse(int code, nuint message, nint data)
        {
            try
            {
                if (code >= 0 && message == 0x0202 && _current is { } observer) observer.Signal(observer._released);
            }
            catch (Exception error)
            {
                TraceFailure("Capture mouse observation failed: {0}", error);
            }

            return NativeMethods.CallNextHookEx(0, code, message, data);
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static unsafe nint OnKeyboard(int code, nuint message, nint data)
        {
            try
            {
                if (code >= 0 && message is 0x0100 or 0x0104 && data != 0 && *(uint*)data == 0x1B &&
                    _current is { } observer)
                    observer.Signal(observer._cancelled);
            }
            catch (Exception error)
            {
                TraceFailure("Capture keyboard observation failed: {0}", error);
            }

            return NativeMethods.CallNextHookEx(0, code, message, data);
        }

        private static void TraceFailure(string message, Exception error)
        {
            try
            {
                Trace.TraceError(message, error);
            }
            catch (Exception)
            {
                // A failing listener must not unwind through a native input callback.
            }
        }
    }
}

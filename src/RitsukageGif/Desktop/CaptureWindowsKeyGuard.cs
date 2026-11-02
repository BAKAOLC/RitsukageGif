using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal sealed partial class CaptureWindowsKeyGuard : IDisposable
    {
        private const int InvalidHookHandle = 1404;
        private static readonly ILogger Logger = LogHub.GetLogger("Desktop.Capture.Input");
        private static readonly ConcurrentDictionary<CaptureWindowsKeyGuard, byte> Active = new();
        private readonly CaptureKeyboardHookCallback _callback;
        private readonly nint _desktop;
        private readonly CaptureBlockedWindowsKeys _keys;
        private readonly Lock _lifetime = new();
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenSource _stop = new();
        private readonly Thread _thread;
        private CaptureBlockedWindowsKeys _blocked;
        private CancellationTokenRegistration _cancellationRegistration;
        private int _disposed;
        private nint _hook;
        private uint _nativeThreadId;
        private bool _nativeFinished;
        private nint[] _windows;

        private CaptureWindowsKeyGuard(CaptureBlockedWindowsKeys keys, ReadOnlyMemory<nint> windows)
        {
            _keys = keys;
            _windows = windows.ToArray();
            _desktop = GetThreadDesktop(GetCurrentThreadId());
            if (_desktop == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
            _callback = OnKeyboard;
            _thread = new(Run) { IsBackground = true, Name = "Capture Windows-key guard" };
        }

        internal Task Completion => _completion.Task;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            Volatile.Write(ref _windows, []);
            lock (_lifetime)
                if (!_nativeFinished)
                    _stop.Cancel();
        }

        internal static async Task<CaptureWindowsKeyGuard?> CreateAsync(CaptureBlockedWindowsKeys keys,
            ReadOnlyMemory<nint> windows, CancellationToken token = default)
        {
            if (keys == CaptureBlockedWindowsKeys.None || !OperatingSystem.IsWindows()) return null;
            token.ThrowIfCancellationRequested();
            var guard = new CaptureWindowsKeyGuard(keys, windows);
            guard._cancellationRegistration = token.Register(static state =>
                ((CaptureWindowsKeyGuard)state!).Dispose(), guard);
            try
            {
                guard._thread.Start();
            }
            catch
            {
                guard.FinishLifetime();
                throw;
            }

            try
            {
                await guard._ready.Task.ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                return guard;
            }
            catch
            {
                guard.Dispose();
                await guard.Completion.ConfigureAwait(false);
                throw;
            }
        }

        internal void UpdateAllowedWindows(ReadOnlySpan<nint> windows)
        {
            if (Volatile.Read(ref _disposed) == 0) Volatile.Write(ref _windows, [.. windows]);
        }

        private unsafe void Run()
        {
            Exception? failure = null;
            try
            {
                _nativeThreadId = GetCurrentThreadId();
                if (SetThreadDesktop(_desktop) == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
                NativeMethods.PeekMessageW(out _, 0, 0, 0, 0);
                if (_stop.IsCancellationRequested) return;
                _hook = NativeMethods.SetMouseHook(13, Marshal.GetFunctionPointerForDelegate(_callback),
                    NativeMethods.MouseHookModule(0), 0);
                if (_hook == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
                Active.TryAdd(this, 0);
                if (_stop.IsCancellationRequested) return;
                _ready.TrySetResult();
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
                            throw new InvalidOperationException("Capture Windows-key observation ended unexpectedly.");
                }
            }
            catch (Exception error)
            {
                failure = error;
                _ready.TrySetException(error);
                TraceFailure(error);
            }
            finally
            {
                if (_hook != 0)
                {
                    var removed = NativeMethods.RemoveMouseHook(_hook) != 0;
                    if (!removed)
                    {
                        var error = new Win32Exception(Marshal.GetLastPInvokeError());
                        TraceFailure(error);
                        removed = error.NativeErrorCode == InvalidHookHandle;
                        if (!removed) failure ??= error;
                    }

                    if (removed)
                    {
                        _hook = 0;
                        Active.TryRemove(this, out _);
                    }
                }

                _ready.TrySetCanceled();
                FinishLifetime();
                if (failure is not null) _completion.TrySetException(failure);
                else _completion.TrySetResult();
                GC.KeepAlive(_callback);
            }
        }

        private void FinishLifetime()
        {
            _cancellationRegistration.Dispose();
            lock (_lifetime)
            {
                _nativeFinished = true;
                _stop.Dispose();
            }
        }

        private unsafe nint OnKeyboard(int code, nuint message, nint data)
        {
            if (code >= 0 && data != 0 && message is 0x0100 or 0x0101 or 0x0104 or 0x0105)
            {
                var virtualKey = *(uint*)data;
                if (virtualKey is 0x5B or 0x5C &&
                    SuppressKey(virtualKey, message is 0x0100 or 0x0104, GetForegroundWindow()))
                    return 1;
            }

            return NativeMethods.CallNextHookEx(0, code, message, data);
        }

        private bool SuppressKey(uint virtualKey, bool down, nint foreground)
        {
            var key = virtualKey switch
            {
                0x5B => CaptureBlockedWindowsKeys.Left,
                0x5C => CaptureBlockedWindowsKeys.Right,
                _ => CaptureBlockedWindowsKeys.None,
            };
            if (key == CaptureBlockedWindowsKeys.None) return false;
            if (!down)
            {
                var blocked = (_blocked & key) != 0;
                _blocked &= ~key;
                return blocked;
            }

            if ((_blocked & key) != 0) return true;
            if ((_keys & key) == 0 || Volatile.Read(ref _disposed) != 0) return false;
            var windows = Volatile.Read(ref _windows);
            foreach (var window in windows)
                if (foreground != 0 && window == foreground)
                {
                    _blocked |= key;
                    return true;
                }

            return false;
        }

        private void TraceFailure(Exception error)
        {
            Logger.ZLogWarning(error,
                $"Capture Windows-key guard failed. NativeThreadId={_nativeThreadId}");
        }

        [LibraryImport("user32.dll")]
        private static partial nint GetForegroundWindow();

        [LibraryImport("user32.dll", SetLastError = true)]
        private static partial nint GetThreadDesktop(uint thread);

        [LibraryImport("user32.dll", SetLastError = true)]
        private static partial int SetThreadDesktop(nint desktop);

        [LibraryImport("kernel32.dll")]
        private static partial uint GetCurrentThreadId();
    }
}

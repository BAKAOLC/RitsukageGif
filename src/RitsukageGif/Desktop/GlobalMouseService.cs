using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Input;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed class GlobalMouseService : IDisposable
    {
        private readonly GlobalMouseHookCallback _callback;
        private readonly Action<string> _error;
        private readonly Func<string, PixelRect?, Task> _execute;
        private readonly Action _registered;
        private readonly Action<string> _registrationError;
        private readonly CancellationTokenSource _stop = new();
        private readonly Thread? _thread;
        private readonly GlobalMouseGestureTracker _tracker;
        private int _disposed;

        internal GlobalMouseService(IReadOnlyList<GlobalMouseBinding> bindings,
            IReadOnlyList<GlobalActionBinding> actions, Func<string, PixelRect?, Task> execute, Action<string> error,
            Action<string> registrationError, Action registered)
        {
            var failures = Validate(bindings, actions);
            if (failures.Count > 0) throw new ArgumentException(string.Join(Environment.NewLine, failures));
            var active = new Dictionary<InputMouseGesture, string>();
            foreach (var binding in bindings.Where(binding => binding.Enabled &&
                                                              actions.Any(action =>
                                                                  action.Enabled && action.Id == binding.ActionId)))
            {
                if (!InputMouseGesture.TryParse(binding.Gesture, out var gesture))
                    throw new ArgumentException(UiText.Get("GlobalMouseInvalidBinding"));
                active.Add(gesture, binding.ActionId);
            }

            _execute = execute;
            _error = error;
            _registrationError = registrationError;
            _registered = registered;
            _callback = OnMouse;
            _tracker = new(active, Supported ? Math.Max(1, NativeMethods.GetSystemMetrics(68)) : 4,
                Supported ? Math.Max(1, NativeMethods.GetSystemMetrics(69)) : 4);
            if (active.Count == 0)
            {
                _registered();
                return;
            }

            if (!Supported) throw new PlatformNotSupportedException(UiText.Get("GlobalMouseUnsupported"));
            _thread = new(Run) { IsBackground = true, Name = "Global mouse gestures" };
            _thread.Start();
        }

        internal static bool Supported => OperatingSystem.IsWindows();

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _stop.Cancel();
            _thread?.Join();
            _stop.Dispose();
        }

        internal static IReadOnlyList<string> Validate(IReadOnlyList<GlobalMouseBinding> bindings,
            IReadOnlyList<GlobalActionBinding> actions)
        {
            var errors = new List<string>();
            var gestures = new HashSet<InputMouseGesture>();
            var actionIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var binding in bindings)
            {
                if (!binding.Enabled) continue;
                if (actions.All(action => action.Id != binding.ActionId))
                    errors.Add(UiText.Get("GlobalMouseActionMissing"));
                if (!actionIds.Add(binding.ActionId)) errors.Add(UiText.Get("GlobalMouseConflict"));
                if (!InputMouseGesture.TryParse(binding.Gesture, out var gesture) ||
                    (gesture.Kind & MouseGestureKind.Drags) == 0)
                {
                    errors.Add(UiText.Get("GlobalMouseInvalidBinding") + ": " + binding.Gesture);
                    continue;
                }

                if (gesture.Modifiers == KeyModifiers.None)
                    errors.Add(UiText.Get("GlobalMouseModifierRequired") + ": " + binding.Gesture);
                if (!gestures.Add(gesture)) errors.Add(UiText.Get("GlobalMouseConflict") + ": " + binding.Gesture);
            }

            return errors;
        }

        private unsafe void Run()
        {
            nint hook = 0;
            try
            {
                NativeMethods.PeekMessageW(out _, 0, 0, 0, 0);
                if (_stop.IsCancellationRequested) return;
                hook = NativeMethods.SetMouseHook(14, Marshal.GetFunctionPointerForDelegate(_callback),
                    NativeMethods.MouseHookModule(0), 0);
                if (hook == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
                Dispatcher.UIThread.Post(() =>
                {
                    if (!_stop.IsCancellationRequested) _registered();
                });
                var handle = _stop.Token.WaitHandle.SafeWaitHandle.DangerousGetHandle();
                while (!_stop.IsCancellationRequested)
                {
                    var result = NativeMethods.MsgWaitForMultipleObjectsEx(1, &handle, uint.MaxValue, 0x04FF, 4);
                    switch (result)
                    {
                        case 0: return;
                        case uint.MaxValue: throw new Win32Exception(Marshal.GetLastPInvokeError());
                    }

                    while (!_stop.IsCancellationRequested &&
                           NativeMethods.PeekMessageW(out var message, 0, 0, 0, 1) != 0)
                        if (message.Id == 0x0012)
                            return;
                }
            }
            catch (Exception exception)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (!_stop.IsCancellationRequested) _registrationError(exception.Message);
                });
            }
            finally
            {
                if (hook != 0 && NativeMethods.RemoveMouseHook(hook) == 0)
                    Trace.TraceError("Could not remove the global mouse hook before thread exit.");
                GC.KeepAlive(_callback);
            }
        }

        private unsafe nint OnMouse(int code, nuint message, nint pointer)
        {
            if (code >= 0 && !_stop.IsCancellationRequested)
                try
                {
                    var modifiers = message is 0x0201 or 0x0204 or 0x0207 or 0x020B
                        ? ReadModifiers()
                        : KeyModifiers.None;
                    var action = _tracker.Process((uint)message, *(GlobalMouseHookData*)pointer, modifiers,
                        out var region);
                    if (action is not null)
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (!_stop.IsCancellationRequested) _ = ExecuteAsync(action, region);
                        });
                }
                catch (Exception exception)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (!_stop.IsCancellationRequested) _error(exception.Message);
                    });
                }

            return NativeMethods.CallNextHookEx(0, code, message, pointer);
        }

        private static KeyModifiers ReadModifiers()
        {
            var modifiers = KeyModifiers.None;
            if (NativeMethods.GetAsyncKeyState(0x11) < 0) modifiers |= KeyModifiers.Control;
            if (NativeMethods.GetAsyncKeyState(0x10) < 0) modifiers |= KeyModifiers.Shift;
            if (NativeMethods.GetAsyncKeyState(0x12) < 0) modifiers |= KeyModifiers.Alt;
            if (NativeMethods.GetAsyncKeyState(0x5B) < 0 || NativeMethods.GetAsyncKeyState(0x5C) < 0)
                modifiers |= KeyModifiers.Meta;
            return modifiers;
        }

        private async Task ExecuteAsync(string action, PixelRect? region)
        {
            try
            {
                await _execute(action, region);
            }
            catch (Exception exception)
            {
                if (!_stop.IsCancellationRequested) _error(exception.Message);
            }
        }
    }
}

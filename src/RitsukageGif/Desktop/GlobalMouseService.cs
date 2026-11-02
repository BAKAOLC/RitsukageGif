using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Input;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed class GlobalMouseService : IDisposable
    {
        private static readonly ILogger MouseLogger = LogHub.GetLogger("Desktop.Input.GlobalMouse");
        private readonly GlobalMouseHookCallback _callback;
        private readonly Action<string> _error;
        private readonly Func<string, PixelRect?, Task> _execute;
        private readonly Action<string?, PixelRect?>? _preview;
        private readonly Lock _previewGate = new();
        private readonly Action _registered;
        private readonly Action<string> _registrationError;
        private readonly CancellationTokenSource _stop = new();
        private readonly Thread? _thread;
        private readonly GlobalMouseGestureTracker _tracker;
        private int _disposed;
        private string? _previewAction;
        private bool _previewQueued;
        private PixelRect? _previewRegion;

        internal GlobalMouseService(IReadOnlyList<GlobalMouseBinding> bindings,
            IReadOnlyList<GlobalActionBinding> actions, Func<string, PixelRect?, Task> execute, Action<string> error,
            Action<string> registrationError, Action registered, Action<string?, PixelRect?>? preview = null)
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
            _preview = preview;
            _callback = OnMouse;
            _tracker = new(active, OperatingSystem.IsWindows() ? Math.Max(1, NativeMethods.GetSystemMetrics(68)) : 4,
                OperatingSystem.IsWindows() ? Math.Max(1, NativeMethods.GetSystemMetrics(69)) : 4);
            if (active.Count == 0)
            {
                _registered();
                return;
            }

            if (!Supported) throw new PlatformNotSupportedException(UiText.Get("GlobalMouseUnsupported"));
            _thread = new(Run) { IsBackground = true, Name = "Global mouse gestures" };
            _thread.Start();
        }

        internal static bool Supported => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ||
                                          (OperatingSystem.IsLinux() && !DesktopCapture.UsesSourceSelection);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _stop.Cancel();
            _thread?.Join();
            if (_preview is not null)
            {
                if (Dispatcher.UIThread.CheckAccess()) _preview(null, null);
                else Dispatcher.UIThread.Post(() => _preview(null, null));
            }

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
                if (!OperatingSystem.IsWindows())
                {
                    RunPortable();
                    return;
                }

                NativeMethods.PeekMessageW(out _, 0, 0, 0, 0);
                if (_stop.IsCancellationRequested) return;
                hook = NativeMethods.SetMouseHook(14, Marshal.GetFunctionPointerForDelegate(_callback),
                    NativeMethods.MouseHookModule(0), 0);
                if (hook == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
                ReportRegistered();
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
                if (_preview is not null)
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (Volatile.Read(ref _disposed) == 0) _preview(null, null);
                    });
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
                    ProcessNativeInput((uint)message, *(GlobalMouseHookData*)pointer, modifiers);
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

        private void RunPortable()
        {
            var delay = 1;
            while (!_stop.IsCancellationRequested)
                try
                {
                    PortableGlobalMouseObserver.Run(ProcessNativeInput, () =>
                    {
                        delay = 1;
                        ReportRegistered();
                    }, _stop.Token);
                    if (!_stop.IsCancellationRequested)
                        throw new IOException("The global mouse observation session closed.");
                }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    if (_stop.IsCancellationRequested) return;
                    MouseLogger.ZLogWarning(exception, $"Global mouse observation interrupted. RetrySeconds={delay}");
                    ProcessNativeInput(0x001F, default, KeyModifiers.None);
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (!_stop.IsCancellationRequested) _registrationError(exception.Message);
                    });
                    if (_stop.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(delay))) return;
                    delay = Math.Min(30, delay * 2);
                }
        }

        private void ReportRegistered()
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (!_stop.IsCancellationRequested) _registered();
            });
        }

        private void ProcessNativeInput(uint message, GlobalMouseHookData data, KeyModifiers modifiers)
        {
            if (_stop.IsCancellationRequested) return;
            var action = _tracker.Process(message, data, modifiers, out var region);
            QueuePreview();
            if (action is not null)
                Dispatcher.UIThread.Post(() =>
                {
                    if (!_stop.IsCancellationRequested) _ = ExecuteAsync(action, region);
                });
        }

        private void QueuePreview()
        {
            if (_preview is null) return;
            lock (_previewGate)
            {
                if (_previewAction == _tracker.PreviewAction && _previewRegion == _tracker.PreviewRegion) return;
                _previewAction = _tracker.PreviewAction;
                _previewRegion = _tracker.PreviewRegion;
                if (_previewQueued) return;
                _previewQueued = true;
            }

            Dispatcher.UIThread.Post(() =>
            {
                string? action;
                PixelRect? region;
                lock (_previewGate)
                {
                    _previewQueued = false;
                    action = _previewAction;
                    region = _previewRegion;
                }

                if (Volatile.Read(ref _disposed) == 0) _preview(action, region);
            });
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

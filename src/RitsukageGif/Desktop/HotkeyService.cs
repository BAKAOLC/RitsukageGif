using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed class HotkeyService : IDisposable
    {
        private static readonly ILogger HotkeyLogger = LogHub.GetLogger("Desktop.Input.Hotkeys");
        private readonly IDisposable? _native;
        private readonly CancellationTokenSource _stop = new();
        private readonly Thread? _thread;
        private int _disposed;

        public HotkeyService(IReadOnlyList<(string Shortcut, Func<Task> Action)> bindings, Action<string> error,
            Action<string> registrationError, Action registered,
            Action<HotkeyRegistrationResult>? registrationChanged = null, Func<string, string>? describe = null)
        {
            HotkeyLogger.ZLogDebug($"Global hotkey registration requested. BindingCount={bindings.Count}");
            Validate(bindings.Select(b => b.Shortcut));
            var keys = bindings.Select(b => Parse(b.Shortcut)).ToArray();
            var cancellation = _stop.Token;
            var portal = OperatingSystem.IsLinux() && DesktopCapture.UsesSourceSelection;
            var portalTriggers = portal ? new Dictionary<int, string>() : null;
            var descriptions = portal
                ? bindings.Select(binding => describe?.Invoke(binding.Shortcut) ?? binding.Shortcut).ToArray()
                : null;
            if (OperatingSystem.IsMacOS())
            {
                try
                {
                    _native = PlatformHotkeys.RegisterMac(keys, index => Dispatcher.UIThread.Post(() =>
                    {
                        if (!cancellation.IsCancellationRequested) _ = ExecuteAsync(bindings[index].Action, error);
                    }), ReportRegistrationError, ReportRegistered, ReportBinding);
                }
                catch
                {
                    _stop.Cancel();
                    _stop.Dispose();
                    throw;
                }

                return;
            }

            _thread = new(() =>
            {
                if (!OperatingSystem.IsWindows())
                {
                    try
                    {
                        PlatformHotkeys.Run(keys, index => Dispatcher.UIThread.Post(() =>
                            {
                                if (!cancellation.IsCancellationRequested)
                                    _ = ExecuteAsync(bindings[index].Action, error);
                            }), ReportRegistrationError, ReportRegistered, cancellation, ReportBinding,
                            (index, trigger) => portalTriggers![index] = trigger, descriptions);
                    }
                    catch (Exception exception)
                    {
                        ReportRegistrationError(exception.Message);
                        for (var index = 0; index < bindings.Count; index++) ReportBinding(index, exception.Message);
                    }

                    return;
                }

                try
                {
                    RunWindows(keys, bindings, error, ReportRegistrationError, ReportRegistered, ReportBinding);
                }
                catch (Exception exception)
                {
                    HotkeyLogger.ZLogError(exception, $"Global hotkey registration failed.");
                    ReportRegistrationError(exception.Message);
                    for (var index = 0; index < bindings.Count; index++) ReportBinding(index, exception.Message);
                }
            }) { IsBackground = true, Name = "Global hotkeys" };
            _thread.Start();
            return;

            void ReportRegistrationError(string message)
            {
                HotkeyLogger.ZLogWarning($"Global hotkey registration unavailable. Error={message}");
                Dispatcher.UIThread.Post(() =>
                {
                    if (!cancellation.IsCancellationRequested) registrationError(message);
                });
            }

            void ReportBinding(int index, string? message)
            {
                if (registrationChanged is null) return;
                var result = new HotkeyRegistrationResult(bindings[index].Shortcut,
                        message is null ? HotkeyRegistrationState.Registered : HotkeyRegistrationState.Failed, message)
                    { TriggerDescription = portalTriggers?.GetValueOrDefault(index) };
                Dispatcher.UIThread.Post(() =>
                {
                    if (!cancellation.IsCancellationRequested) registrationChanged(result);
                });
            }

            void ReportRegistered()
            {
                HotkeyLogger.ZLogDebug($"Global hotkeys registered. BindingCount={bindings.Count}");
                Dispatcher.UIThread.Post(() =>
                {
                    if (!cancellation.IsCancellationRequested) registered();
                });
            }
        }

        internal static bool Supported =>
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || OperatingSystem.IsLinux();

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            HotkeyLogger.ZLogDebug($"Global hotkey registration stopping.");
            _stop.Cancel();
            try
            {
                _native?.Dispose();
            }
            finally
            {
                _thread?.Join();
                _stop.Dispose();
            }
        }

        private unsafe void RunWindows((uint Modifiers, uint Key)[] keys,
            IReadOnlyList<(string Shortcut, Func<Task> Action)> bindings, Action<string> error,
            Action<string> registrationError, Action ready, Action<int, string?> registrationChanged)
        {
            var registered = new HashSet<int>();
            var failed = false;
            // Create the thread queue before registering. Cancellation uses an event, not a posted message.
            NativeMethods.PeekMessageW(out _, 0, 0, 0, 0);
            var token = _stop.Token;
            var cancellationHandle = token.WaitHandle.SafeWaitHandle.DangerousGetHandle();
            try
            {
                for (var index = 0; index < keys.Length && !token.IsCancellationRequested; index++)
                {
                    if (keys[index].Key == 0) continue;
                    if (NativeMethods.RegisterHotKey(0, index + 1, keys[index].Modifiers | 0x4000,
                            keys[index].Key) != 0)
                    {
                        registered.Add(index + 1);
                        registrationChanged(index, null);
                    }
                    else
                    {
                        var shortcut = bindings[index].Shortcut;
                        var nativeError = Marshal.GetLastPInvokeError();
                        failed = true;
                        var message = UiText.Get("HotkeyOccupied") + " " + shortcut + ": " +
                                      new Win32Exception(nativeError).Message;
                        registrationChanged(index, message);
                        registrationError(message);
                    }
                }

                if (!failed && !token.IsCancellationRequested) ready();
                while (!token.IsCancellationRequested)
                {
                    var result = NativeMethods.MsgWaitForMultipleObjectsEx(1, &cancellationHandle,
                        uint.MaxValue, 0x04FF, 0x0004);
                    switch (result)
                    {
                        case 0: return;
                        case uint.MaxValue: throw new Win32Exception(Marshal.GetLastPInvokeError());
                    }

                    while (!token.IsCancellationRequested &&
                           NativeMethods.PeekMessageW(out var message, 0, 0, 0, 1) != 0)
                    {
                        if (message.Id == 0x0012) return;
                        if (message.Id != 0x0312 || message.WParam > int.MaxValue ||
                            !registered.Contains((int)message.WParam)) continue;
                        var action = bindings[(int)message.WParam - 1].Action;
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (!_stop.IsCancellationRequested) _ = ExecuteAsync(action, error);
                        });
                    }
                }
            }
            finally
            {
                foreach (var id in registered)
                    if (NativeMethods.UnregisterHotKey(0, id) == 0)
                        Trace.TraceWarning("Could not unregister global hotkey {0} before thread exit.", id);
            }
        }

        private static async Task ExecuteAsync(Func<Task> action, Action<string> error)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                error(ex.Message);
            }
        }

        public static void Validate(IEnumerable<string> shortcuts)
        {
            var seen = new HashSet<(uint, uint)>();
            foreach (var shortcut in shortcuts)
            {
                var key = Parse(shortcut);
                if (key.Key != 0 && !seen.Add(key)) throw new ArgumentException(UiText.Get("DuplicateHotkeys"));
            }
        }

        public static (uint Modifiers, uint Key) Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return (0, 0);
            uint modifiers = 0, key = 0;
            foreach (var part in text.Split('+',
                         StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                switch (part.ToUpperInvariant())
                {
                    case "CTRL":
                    case "CONTROL": modifiers |= 2; break;
                    case "SHIFT": modifiers |= 4; break;
                    case "ALT": modifiers |= 1; break;
                    case "WIN":
                    case "WINDOWS":
                    case "CMD":
                    case "META":
                    case "SUPER": modifiers |= 8; break;
                    default:
                        if (key != 0) throw new ArgumentException(UiText.Get("HotkeySingleKey"));
                        var upper = part.ToUpperInvariant();
                        if (upper.Length == 1 && char.IsAsciiLetterOrDigit(upper[0])) key = upper[0];
                        else if (upper.StartsWith('F') && int.TryParse(upper[1..], out var f) && f is >= 1 and <= 24)
                            key = (uint)(0x70 + f - 1);
                        else if (HotkeyKeyMap.TryParse(upper, out var mapped)) key = mapped;
                        else throw new ArgumentException(UiText.Get("HotkeySupported"));
                        break;
                }

            if (key == 0) throw new ArgumentException(UiText.Get("HotkeySingleKey"));
            return (modifiers, key);
        }
    }
}

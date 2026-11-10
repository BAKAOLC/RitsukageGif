using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia.Input;
using Avalonia.Win32.Input;

namespace RitsukageGif.Desktop
{
    internal sealed partial class RecordingInputObserver
    {
        [ThreadStatic] private static RecordingInputObserver? _windowsCurrent;
        private long _windowsTimestamp;
        private ulong _windowsTime;
        private uint _windowsModifiers;

        private KeyModifiers WindowsModifiers =>
            ((_windowsModifiers & 3) != 0 ? KeyModifiers.Shift : 0) |
            ((_windowsModifiers & 12) != 0 ? KeyModifiers.Control : 0) |
            ((_windowsModifiers & 48) != 0 ? KeyModifiers.Alt : 0) |
            ((_windowsModifiers & 192) != 0 ? KeyModifiers.Meta : 0);

        private unsafe void RunWindows()
        {
            nint mouse = 0, keyboard = 0;
            _windowsCurrent = this;
            try
            {
                NativeMethods.PeekMessageW(out _, 0, 0, 0, 0);
                var before = Stopwatch.GetTimestamp();
                _windowsTime = GetRecordingTickCount();
                _windowsTimestamp = before + (Stopwatch.GetTimestamp() - before) / 2;
                uint[] modifierKeys = [0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x5B, 0x5C];
                for (var index = 0; index < modifierKeys.Length; index++)
                    if (NativeMethods.GetAsyncKeyState((int)modifierKeys[index]) < 0)
                        _windowsModifiers |= 1u << index;
                var module = NativeMethods.MouseHookModule(0);
                if (_recordMouse)
                {
                    mouse = NativeMethods.SetMouseHook(14,
                        (nint)(delegate* unmanaged[Stdcall]<int, nuint, nint, nint>)&RecordingMouse, module, 0);
                    if (mouse == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
                }

                // Modifier transitions are also needed for mouse-only observation.
                keyboard = NativeMethods.SetMouseHook(13,
                    (nint)(delegate* unmanaged[Stdcall]<int, nuint, nint, nint>)&RecordingKeyboard, module, 0);
                if (keyboard == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
                _stop.Token.ThrowIfCancellationRequested();
                _ready.TrySetResult();
                var signal = _stop.Token.WaitHandle.SafeWaitHandle.DangerousGetHandle();
                while (!_stop.IsCancellationRequested)
                {
                    var wait = NativeMethods.MsgWaitForMultipleObjectsEx(1, &signal, uint.MaxValue, 0x04FF, 4);
                    if (wait == 0) break;
                    if (wait == uint.MaxValue) throw new Win32Exception(Marshal.GetLastPInvokeError());
                    while (NativeMethods.PeekMessageW(out var message, 0, 0, 0, 1) != 0)
                        if (message.Id == 0x0012)
                            throw new IOException("Recording input observation stopped unexpectedly.");
                }
            }
            finally
            {
                if (keyboard != 0 && NativeMethods.RemoveMouseHook(keyboard) == 0)
                    Interlocked.CompareExchange(ref _failure, new Win32Exception(Marshal.GetLastPInvokeError()), null);
                if (mouse != 0 && NativeMethods.RemoveMouseHook(mouse) == 0)
                    Interlocked.CompareExchange(ref _failure, new Win32Exception(Marshal.GetLastPInvokeError()), null);
                _windowsCurrent = null;
            }
        }

        private long WindowsTimestamp(uint time)
        {
            return _windowsTimestamp + unchecked((int)(time - (uint)_windowsTime)) * Stopwatch.Frequency / 1000;
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static unsafe nint RecordingKeyboard(int code, nuint message, nint data)
        {
            try
            {
                if (code >= 0 && data != 0 && _windowsCurrent is { } observer &&
                    message is 0x0100 or 0x0101 or 0x0104 or 0x0105)
                {
                    var input = *(RecordingKeyboardHookData*)data;
                    if ((input.Flags & 0x10) == 0)
                    {
                        var keyData = (int)(input.ScanCode << 16) | ((input.Flags & 1) != 0 ? 1 << 24 : 0);
                        var key = KeyInterop.PhysicalKeyFromVirtualKey((int)input.VirtualKey, keyData).ToQwertyKey();
                        if (key == Key.None) key = KeyInterop.KeyFromVirtualKey((int)input.VirtualKey, keyData);
                        var down = message is 0x0100 or 0x0104;
                        var bit = key switch
                        {
                            Key.LeftShift => 1u, Key.RightShift => 2u, Key.LeftCtrl => 4u, Key.RightCtrl => 8u,
                            Key.LeftAlt => 16u, Key.RightAlt => 32u, Key.LWin => 64u, Key.RWin => 128u, _ => 0u,
                        };
                        if (down) observer._windowsModifiers |= bit;
                        else observer._windowsModifiers &= ~bit;
                        if (observer._recordKeyboard)
                            observer.Emit(new(observer.WindowsTimestamp(input.Time),
                                down ? RecordingInputEventKind.KeyDown : RecordingInputEventKind.KeyUp,
                                key, observer.WindowsModifiers, NativeCode: input.VirtualKey,
                                NativeScanCode: input.ScanCode));
                    }
                }
            }
            catch (Exception error)
            {
                _windowsCurrent?.Fail(error);
            }

            return NativeMethods.CallNextHookEx(0, code, message, data);
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static unsafe nint RecordingMouse(int code, nuint message, nint data)
        {
            try
            {
                if (code >= 0 && data != 0 && _windowsCurrent is { } observer)
                {
                    var input = *(GlobalMouseHookData*)data;
                    if ((input.Flags & 1) == 0)
                    {
                        var button = message switch
                        {
                            0x0201 or 0x0202 => 1, 0x0204 or 0x0205 => 2, 0x0207 or 0x0208 => 4,
                            0x020B or 0x020C => input.MouseData >> 16 == 1 ? 8 : 16, _ => 0,
                        };
                        if (button != 0)
                            observer.Emit(new(observer.WindowsTimestamp(input.Time),
                                message is 0x0201 or 0x0204 or 0x0207 or 0x020B
                                    ? RecordingInputEventKind.PointerDown
                                    : RecordingInputEventKind.PointerUp,
                                Modifiers: observer.WindowsModifiers, Button: button, X: input.X, Y: input.Y));
                        else if (message is 0x020A or 0x020E)
                            observer.Emit(new(observer.WindowsTimestamp(input.Time), RecordingInputEventKind.Wheel,
                                Modifiers: observer.WindowsModifiers, X: input.X, Y: input.Y,
                                WheelDelta: (short)(input.MouseData >> 16), NativeCode: (uint)message));
                    }
                }
            }
            catch (Exception error)
            {
                _windowsCurrent?.Fail(error);
            }

            return NativeMethods.CallNextHookEx(0, code, message, data);
        }

        [LibraryImport("kernel32.dll", EntryPoint = "GetTickCount64")]
        private static partial ulong GetRecordingTickCount();
    }
}

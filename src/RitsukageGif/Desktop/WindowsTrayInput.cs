using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal sealed unsafe partial class WindowsTrayInput : IDisposable
    {
        private const uint TrayMessage = 0x800;
        private const nuint SubclassId = 0x52475254;
        private static readonly Dictionary<nint, WindowsTrayInput> Instances = [];
        [ThreadStatic] private static nint _candidate;
        private readonly Action<uint> _message;
        private readonly nint _window;
        private bool _disposed;
        private nuint _icon;

        private WindowsTrayInput(nint window, Action<uint> message)
        {
            _window = window;
            _message = message;
        }

        internal static TimeSpan DoubleClickTime => OperatingSystem.IsWindows()
            ? TimeSpan.FromMilliseconds(GetDoubleClickTime())
            : TimeSpan.FromMilliseconds(500);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Instances.Remove(_window);
            if (RemoveWindowSubclass(_window, &WindowProcedure, SubclassId) == 0 && IsWindow(_window) != 0)
                Trace.TraceWarning("Failed to remove the tray input window subclass.");
        }

        internal static WindowsTrayInput? Attach(Action<uint> message)
        {
            if (!OperatingSystem.IsWindows()) return null;
            _candidate = 0;
            var completed = EnumThreadWindows(GetCurrentThreadId(), &FindWindow, 0);
            if (completed == 0 && _candidate == 0) return null;
            if (_candidate == 0 || Instances.ContainsKey(_candidate)) return null;
            var result = new WindowsTrayInput(_candidate, message);
            if (SetWindowSubclass(_candidate, &WindowProcedure, SubclassId, 0) == 0) return null;
            Instances.Add(_candidate, result);
            return result;
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static int FindWindow(nint window, nint parameter)
        {
            Span<char> text = stackalloc char[256];
            fixed (char* buffer = text)
            {
                var length = GetClassNameW(window, buffer, text.Length);
                if (length > 0 && text[..length].StartsWith("AvaloniaMessageWindow ", StringComparison.Ordinal))
                {
                    _candidate = window;
                    return 0;
                }
            }

            return 1;
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static nint WindowProcedure(nint window, uint message, nuint first, nint second,
            nuint subclass, nuint data)
        {
            try
            {
                if (message == TrayMessage && first != 0 && Instances.TryGetValue(window, out var instance))
                {
                    if (instance._icon == 0) instance._icon = first;
                    if (first == instance._icon && (uint)second is 0x202 or 0x203 or 0x208)
                    {
                        instance._message((uint)second);
                        return 0;
                    }
                }
            }
            catch (Exception)
            {
                // Native callbacks must never unwind into the Windows message loop.
            }

            return DefSubclassProc(window, message, first, second);
        }

        [LibraryImport("kernel32.dll")]
        private static partial uint GetCurrentThreadId();

        [LibraryImport("user32.dll")]
        private static partial int EnumThreadWindows(uint thread,
            delegate* unmanaged[Stdcall]<nint, nint, int> callback, nint parameter);

        [LibraryImport("user32.dll")]
        private static partial int IsWindow(nint window);

        [LibraryImport("user32.dll")]
        private static partial int GetClassNameW(nint window, char* buffer, int length);

        [LibraryImport("user32.dll")]
        private static partial uint GetDoubleClickTime();

        [LibraryImport("comctl32.dll")]
        private static partial int SetWindowSubclass(nint window,
            delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nuint, nuint, nint> procedure,
            nuint id, nuint data);

        [LibraryImport("comctl32.dll")]
        private static partial int RemoveWindowSubclass(nint window,
            delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nuint, nuint, nint> procedure, nuint id);

        [LibraryImport("comctl32.dll")]
        private static partial nint DefSubclassProc(nint window, uint message, nuint first, nint second);
    }
}

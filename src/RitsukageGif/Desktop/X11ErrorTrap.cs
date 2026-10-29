using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal static unsafe partial class X11ErrorTrap
    {
        private static readonly Lock Gate = new();
        private static readonly ConcurrentDictionary<nint, State> Displays = new();
        private static bool _initialized;
        private static nint _previous;

        public static void Initialize()
        {
            lock (Gate)
            {
                if (_initialized) return;
                if (XInitThreads() == 0) throw new NotSupportedException(UiText.Get("CapturePlatformUnsupported"));
                _previous = XSetErrorHandler((nint)(delegate* unmanaged[Cdecl]<nint, nint, int>)&OnError);
                _initialized = true;
            }
        }

        public static void Register(nint display)
        {
            Displays.TryAdd(display, new());
        }

        public static void Unregister(nint display)
        {
            Displays.TryRemove(display, out _);
        }

        public static void Clear(nint display)
        {
            if (Displays.TryGetValue(display, out var state)) Volatile.Write(ref state.Error, 0);
        }

        public static bool HasError(nint display)
        {
            return Displays.TryGetValue(display, out var state) && Volatile.Read(ref state.Error) != 0;
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static int OnError(nint display, nint error)
        {
            if (Displays.TryGetValue(display, out var state))
            {
                Volatile.Write(ref state.Error, 1);
                return 0;
            }

            return _previous == 0 ? 0 : ((delegate* unmanaged[Cdecl]<nint, nint, int>)_previous)(display, error);
        }

        [LibraryImport("libX11.so.6")]
        private static partial int XInitThreads();

        [LibraryImport("libX11.so.6")]
        private static partial nint XSetErrorHandler(nint handler);

        private sealed class State
        {
            public int Error;
        }
    }
}

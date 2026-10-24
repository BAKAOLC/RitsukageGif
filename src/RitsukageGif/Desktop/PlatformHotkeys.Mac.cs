using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace RitsukageGif.Desktop
{
    internal static partial class PlatformHotkeys
    {
        private const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";

        internal static unsafe IDisposable RegisterMac((uint Modifiers, uint Key)[] keys, Action<int> pressed,
            Action<string> error)
        {
            var registered = new List<nint>();
            var state = GCHandle.Alloc(new MacCallback(pressed, keys.Length));
            nint handler = 0;
            try
            {
                var target = GetApplicationEventTarget();
                var spec = new EventType { Class = 0x6b657962, Kind = 6 };
                if (InstallEventHandler(target, (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, int>)&MacPressed,
                        1, in spec, GCHandle.ToIntPtr(state), out handler) != 0)
                    throw new InvalidOperationException(UiText.Get("PlatformGlobalHotkeysUnavailable"));
                for (var i = 0; i < keys.Length; i++)
                {
                    if (keys[i].Key == 0) continue;
                    var code = MacKey(keys[i].Key);
                    if (code is null)
                    {
                        error(UiText.Get("HotkeySupported"));
                        continue;
                    }

                    var modifiers = keys[i].Modifiers;
                    var mask = ((modifiers & 1) != 0 ? 1u << 11 : 0) | ((modifiers & 2) != 0 ? 1u << 12 : 0) |
                               ((modifiers & 4) != 0 ? 1u << 9 : 0) | ((modifiers & 8) != 0 ? 1u << 8 : 0);
                    if (RegisterEventHotKey(code.Value, mask, new HotKeyId { Signature = 0x52475348, Id = (uint)i },
                            target, 0, out var handle) == 0)
                        registered.Add(handle);
                    else error(UiText.Get("HotkeyOccupied"));
                }

                return new MacRegistration(registered, handler, state);
            }
            catch
            {
                foreach (var handle in registered) _ = UnregisterEventHotKey(handle);
                if (handler != 0) _ = RemoveEventHandler(handler);
                state.Free();
                throw;
            }
        }

        private sealed class MacRegistration(List<nint> keys, nint handler, GCHandle state) : IDisposable
        {
            private bool _disposed;
            private GCHandle _state = state;

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                foreach (var key in keys) _ = UnregisterEventHotKey(key);
                _ = RemoveEventHandler(handler);
                _state.Free();
            }
        }

        private sealed class MacCallback(Action<int> pressed, int count)
        {
            public void Invoke(int index)
            {
                if (index >= 0 && index < count) pressed(index);
            }
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static int MacPressed(nint next, nint nativeEvent, nint userData)
        {
            try
            {
                if (GetEventParameter(nativeEvent, 0x2d2d2d2d, 0x686b6964, 0, 8, 0, out var id) == 0 &&
                    id.Signature == 0x52475348)
                    (GCHandle.FromIntPtr(userData).Target as MacCallback)?.Invoke((int)id.Id);
            }
            catch
            {
                return -9874;
            }

            return 0;
        }

        private static uint? MacKey(uint key)
        {
            // Apple virtual key codes are physical ANSI key positions.
            var codes = new Dictionary<char, uint>
            {
                ['A'] = 0, ['S'] = 1, ['D'] = 2, ['F'] = 3, ['H'] = 4, ['G'] = 5, ['Z'] = 6, ['X'] = 7, ['C'] = 8,
                ['V'] = 9,
                ['B'] = 11, ['Q'] = 12, ['W'] = 13, ['E'] = 14, ['R'] = 15, ['Y'] = 16, ['T'] = 17,
                ['1'] = 18, ['2'] = 19, ['3'] = 20, ['4'] = 21, ['6'] = 22, ['5'] = 23, ['9'] = 25, ['7'] = 26,
                ['8'] = 28, ['0'] = 29, ['O'] = 31, ['U'] = 32, ['I'] = 34, ['P'] = 35, ['L'] = 37, ['J'] = 38,
                ['K'] = 40, ['N'] = 45, ['M'] = 46
            };
            if (codes.TryGetValue((char)key, out var result)) return result;
            uint[] functions =
                [122, 120, 99, 118, 96, 97, 98, 100, 101, 109, 103, 111, 105, 107, 113, 106, 64, 79, 80, 90];
            return key >= 0x70 && key < 0x70 + functions.Length ? functions[key - 0x70] : HotkeyKeyMap.MacCode(key);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HotKeyId
        {
            public uint Signature, Id;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct EventType
        {
            public uint Class, Kind;
        }

        [LibraryImport(Carbon)]
        private static partial nint GetApplicationEventTarget();

        [LibraryImport(Carbon)]
        private static partial int RegisterEventHotKey(uint key, uint modifiers, HotKeyId id, nint target, uint options,
            out nint reference);

        [LibraryImport(Carbon)]
        private static partial int UnregisterEventHotKey(nint reference);

        [LibraryImport(Carbon)]
        private static partial int InstallEventHandler(nint target, nint callback, uint count, in EventType types,
            nint userData, out nint handler);

        [LibraryImport(Carbon)]
        private static partial int RemoveEventHandler(nint handler);

        [LibraryImport(Carbon)]
        private static partial int GetEventParameter(nint nativeEvent, uint name, uint desiredType, nint actualType,
            uint size, nint actualSize, out HotKeyId data);
    }
}

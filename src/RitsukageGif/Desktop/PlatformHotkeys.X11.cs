using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal static partial class PlatformHotkeys
    {
        private const string X11 = "libX11.so.6";

        private static unsafe void RunX11((uint Modifiers, uint Key)[] keys, CancellationToken token,
            Action<int> pressed, Action<string> error)
        {
            X11ErrorTrap.Initialize();
            var display = XOpenDisplay(0);
            if (display == 0) throw new InvalidOperationException(UiText.Get("PlatformDisplayUnavailable"));
            var registered = new List<(int Index, int Key, uint Mask)>();
            try
            {
                X11ErrorTrap.Register(display);
                var root = XDefaultRootWindow(display);
                for (var i = 0; i < keys.Length; i++)
                {
                    if (keys[i].Key == 0) continue;
                    var name = HotkeyKeyMap.X11Name(keys[i].Key);
                    var code = XKeysymToKeycode(display, XStringToKeysym(name));
                    if (code == 0)
                    {
                        error(UiText.Get("HotkeySupported"));
                        continue;
                    }

                    var modifiers = keys[i].Modifiers;
                    var mask = ((modifiers & 1) != 0 ? 8u : 0) | ((modifiers & 2) != 0 ? 4u : 0) |
                               ((modifiers & 4) != 0 ? 1u : 0) | ((modifiers & 8) != 0 ? 64u : 0);
                    X11ErrorTrap.Clear(display);
                    foreach (var locks in new uint[] { 0, 2, 16, 18 })
                        _ = XGrabKey(display, code, mask | locks, root, 0, 1, 1);
                    _ = XSync(display, 0);
                    if (X11ErrorTrap.HasError(display))
                    {
                        foreach (var locks in new uint[] { 0, 2, 16, 18 })
                            _ = XUngrabKey(display, code, mask | locks, root);
                        error(UiText.Get("HotkeyOccupied"));
                    }
                    else
                    {
                        registered.Add((i, code, mask));
                    }
                }

                var nativeEvent = stackalloc byte[192];
                while (!token.IsCancellationRequested)
                {
                    while (XPending(display) > 0)
                    {
                        _ = XNextEvent(display, (nint)nativeEvent);
                        if (*(int*)nativeEvent != 2) continue;
                        var state = *(uint*)(nativeEvent + 80) & ~18u;
                        var key = *(uint*)(nativeEvent + 84);
                        foreach (var binding in registered)
                            if (binding.Key == key && binding.Mask == state)
                                pressed(binding.Index);
                    }

                    token.WaitHandle.WaitOne(15);
                }

                foreach (var binding in registered)
                foreach (var locks in new uint[] { 0, 2, 16, 18 })
                    _ = XUngrabKey(display, binding.Key, binding.Mask | locks, root);
            }
            finally
            {
                _ = XSync(display, 0);
                X11ErrorTrap.Unregister(display);
                _ = XCloseDisplay(display);
            }
        }

        [LibraryImport(X11)]
        private static partial nint XOpenDisplay(nint name);

        [LibraryImport(X11)]
        private static partial nint XDefaultRootWindow(nint display);

        [LibraryImport(X11)]
        private static partial int XCloseDisplay(nint display);

        [LibraryImport(X11)]
        private static partial int XSync(nint display, int discard);

        [LibraryImport(X11)]
        private static partial int XGrabKey(nint display, int key, uint modifiers, nint window, int ownerEvents,
            int pointerMode, int keyboardMode);

        [LibraryImport(X11)]
        private static partial int XUngrabKey(nint display, int key, uint modifiers, nint window);

        [LibraryImport(X11)]
        private static partial byte XKeysymToKeycode(nint display, nint symbol);

        [LibraryImport(X11, StringMarshalling = StringMarshalling.Utf8)]
        private static partial nint XStringToKeysym(string name);

        [LibraryImport(X11)]
        private static partial int XPending(nint display);

        [LibraryImport(X11)]
        private static partial int XNextEvent(nint display, nint output);
    }
}

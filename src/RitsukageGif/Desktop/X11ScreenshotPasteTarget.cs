using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal sealed partial class X11ScreenshotPasteTarget : IScreenshotPasteTarget
    {
        private static readonly nuint[] ModifierSymbols =
            [0xffe1, 0xffe2, 0xffe3, 0xffe4, 0xffe9, 0xffea, 0xffeb, 0xffec, 0xfe03];
        private readonly nint _focus;
        private readonly uint _processId;
        private readonly nint _notificationWindow;

        private readonly nint _clipboard;
        private readonly int _selectionEvent;
        private nint _display;
        private nint _clipboardOwner;
        private ulong _generation;
        private ulong _copiedGeneration;
        private bool _copied;
        private string? _focusIdentity;

        private X11ScreenshotPasteTarget(nint display, nint focus, uint processId, nint notificationWindow,
            nint clipboard, int selectionEvent)
        {
            _display = display;
            _focus = focus;
            _processId = processId;
            _notificationWindow = notificationWindow;
            _clipboard = clipboard;
            _selectionEvent = selectionEvent;
        }

        public ValueTask MarkClipboardCopiedAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (_display == 0) return ValueTask.CompletedTask;
            ReadSelectionEvents();
            _clipboardOwner = XGetSelectionOwner(_display, _clipboard);
            _copiedGeneration = _generation;
            _copied = _clipboardOwner != 0 && !X11ErrorTrap.HasError(_display);
            return ValueTask.CompletedTask;
        }

        public async Task TryPasteAsync(CancellationToken token)
        {
            if (!_copied || _display == 0 || _focusIdentity is null) return;
            for (var attempt = 0; attempt < 20; attempt++)
            {
                token.ThrowIfCancellationRequested();
                if (!MatchesTarget()) return;
                if (ModifiersReleased()) break;
                if (attempt == 19) return;
                await Task.Delay(15, token);
            }

            var control = XKeysymToKeycode(_display, 0xffe3);
            var paste = XKeysymToKeycode(_display, 0x76);
            var identity = await LinuxAccessibilityLocator.ReadFocusedIdentityAsync(_processId, token);
            if (!string.Equals(identity, _focusIdentity, StringComparison.Ordinal)) return;
            if (control == 0 || paste == 0 || !MatchesTarget() || !ModifiersReleased()) return;
            token.ThrowIfCancellationRequested();
            var controlDown = XTestFakeKeyEvent(_display, control, 1, 0);
            var pasteDown = XTestFakeKeyEvent(_display, paste, 1, 0);
            var pasteUp = XTestFakeKeyEvent(_display, paste, 0, 0);
            var controlUp = XTestFakeKeyEvent(_display, control, 0, 0);
            _ = XSync(_display, 0);
            if (controlDown == 0 || pasteDown == 0 || pasteUp == 0 || controlUp == 0 ||
                X11ErrorTrap.HasError(_display))
                throw new InvalidOperationException("The X11 server rejected the paste keyboard events.");
        }

        public void Dispose()
        {
            var display = Interlocked.Exchange(ref _display, 0);
            if (display != 0) Close(display, _notificationWindow);
        }

        internal static async Task<X11ScreenshotPasteTarget?> CaptureAsync(CancellationToken token)
        {
            var target = await Task.Run(Capture, token);
            if (target is null) return null;
            try
            {
                target._focusIdentity =
                    await LinuxAccessibilityLocator.ReadFocusedIdentityAsync(target._processId, token);
                token.ThrowIfCancellationRequested();
                if (target._focusIdentity is not null && target.MatchesOriginalFocus()) return target;
                target.Dispose();
                return null;
            }
            catch
            {
                target.Dispose();
                throw;
            }
        }

        private static X11ScreenshotPasteTarget? Capture()
        {
            X11ErrorTrap.Initialize();
            var display = XOpenDisplay(0);
            if (display == 0) return null;
            X11ErrorTrap.Register(display);
            nint notificationWindow = 0;
            try
            {
                if (XTestQueryExtension(display, out _, out _, out _, out _) == 0 ||
                    XFixesQueryExtension(display, out var selectionEvent, out _) == 0) return null;
                var major = 1;
                var minor = 0;
                if (XFixesQueryVersion(display, ref major, ref minor) == 0 || major < 1) return null;
                if (XGetInputFocus(display, out var focus, out _) == 0 || focus is 0 or 1) return null;
                var processId = ReadProcess(display, focus);
                if (processId == 0 || processId == Environment.ProcessId) return null;
                notificationWindow = XCreateSimpleWindow(display, XDefaultRootWindow(display), 0, 0, 1, 1, 0, 0, 0);
                if (notificationWindow == 0) return null;
                var clipboard = XInternAtom(display, "CLIPBOARD", 0);
                XFixesSelectSelectionInput(display, notificationWindow, clipboard, 7);
                _ = XSync(display, 0);
                if (X11ErrorTrap.HasError(display)) return null;
                var result = new X11ScreenshotPasteTarget(display, focus, processId, notificationWindow,
                    clipboard, selectionEvent);
                display = notificationWindow = 0;
                return result;
            }
            finally
            {
                if (display != 0) Close(display, notificationWindow);
            }
        }

        private bool MatchesTarget()
        {
            ReadSelectionEvents();
            if (X11ErrorTrap.HasError(_display) || _generation != _copiedGeneration ||
                XGetSelectionOwner(_display, _clipboard) != _clipboardOwner) return false;
            var matches = MatchesOriginalFocus();
            _ = XSync(_display, 0);
            return matches && !X11ErrorTrap.HasError(_display);
        }

        private bool MatchesOriginalFocus()
        {
            return XGetInputFocus(_display, out var focus, out _) != 0 && focus == _focus &&
                   ReadProcess(_display, focus) == _processId;
        }

        private unsafe void ReadSelectionEvents()
        {
            _ = XSync(_display, 0);
            var eventBuffer = stackalloc nint[24];
            while (XPending(_display) > 0)
            {
                _ = XNextEvent(_display, (nint)eventBuffer);
                if (*(int*)eventBuffer == _selectionEvent) _generation++;
            }
        }

        private unsafe bool ModifiersReleased()
        {
            var keys = stackalloc byte[32];
            if (XQueryKeymap(_display, (nint)keys) == 0) return false;
            foreach (var symbol in ModifierSymbols)
            {
                var key = XKeysymToKeycode(_display, symbol);
                if (key != 0 && (keys[key >> 3] & (1 << (key & 7))) != 0) return false;
            }

            return !X11ErrorTrap.HasError(_display);
        }

        private static uint ReadProcess(nint display, nint window)
        {
            var atom = XInternAtom(display, "_NET_WM_PID", 1);
            if (atom == 0) return 0;
            var root = XDefaultRootWindow(display);
            while (window != 0 && window != root)
            {
                var status = XGetWindowProperty(display, window, atom, 0, 1, 0, 6,
                    out var type, out var format, out var count, out _, out var data);
                try
                {
                    if (status != 0) return 0;
                    if (type == 6 && format == 32 && count == 1 && data != 0)
                        return unchecked((uint)Marshal.ReadInt32(data));
                }
                finally
                {
                    if (data != 0) _ = XFree(data);
                }

                if (XQueryTree(display, window, out _, out var parent, out var children, out _) == 0) return 0;
                if (children != 0) _ = XFree(children);
                if (parent == window) return 0;
                window = parent;
            }

            return 0;
        }

        private static void Close(nint display, nint notificationWindow)
        {
            if (notificationWindow != 0) _ = XDestroyWindow(display, notificationWindow);
            _ = XSync(display, 0);
            X11ErrorTrap.Unregister(display);
            _ = XCloseDisplay(display);
        }
    }
}

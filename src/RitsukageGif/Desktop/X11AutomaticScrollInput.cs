using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal sealed partial class X11AutomaticScrollInput : IAutomaticScrollInput, IDisposable
    {
        private readonly Lock _gate = new();
        private readonly WindowTarget _target;
        private readonly uint _process;
        private nint _display;

        private X11AutomaticScrollInput(nint display, WindowTarget target, uint process)
        {
            _display = display;
            _target = target;
            _process = process;
        }

        public string? UnavailableReason { get; private set; }

        public AutomaticScrollInputResult Scroll(ScrollCaptureAxis axis, PixelRect region, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            lock (_gate)
            {
                UnavailableReason = null;
                if (_display == 0) return Unavailable("The X11 input connection is closed.");
                var current = WindowLocator.Refresh(_target);
                if (current is null || current.Client != _target.Client || region.IsEmpty ||
                    region.Intersect(current.Client) != region)
                    return Unavailable("The selected window or capture region changed.");
                X11ErrorTrap.Clear(_display);
                if (ReadProcess(_display, _target.Handle) != _process)
                    return Unavailable("The selected window process changed.");
                var root = XDefaultRootWindow(_display);
                var window = root;
                var targetFound = false;
                int x, y;
                do
                {
                    if (window == _target.Handle) targetFound = true;
                    if (XQueryPointer(_display, window, out _, out var child, out x, out y,
                            out _, out _, out _) == 0)
                        return Unavailable("The current pointer position is unavailable.");
                    window = child;
                } while (window != 0);

                _ = XSync(_display, 0);
                if (X11ErrorTrap.HasError(_display) || !targetFound || !region.Contains(new(x, y)))
                    return Unavailable(
                        "Move the pointer inside the selected capture window to use automatic scrolling.");
                token.ThrowIfCancellationRequested();
                var button = axis == ScrollCaptureAxis.Vertical ? 5u : 7u;
                var pressed = XTestFakeButtonEvent(_display, button, 1, 0);
                var released = XTestFakeButtonEvent(_display, button, 0, 0);
                _ = XSync(_display, 0);
                if (pressed != 0 && released != 0 && !X11ErrorTrap.HasError(_display))
                    return AutomaticScrollInputResult.Sent;
                UnavailableReason = "The X11 server rejected the pointer scroll event.";
                return AutomaticScrollInputResult.Failed;
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_display == 0) return;
                CloseDisplay(_display);
                _display = 0;
            }
        }

        internal static AutomaticScrollConnection Create(PixelRect region, WindowTarget? source)
        {
            if (region.IsEmpty) return new(null, "The capture region is empty.");
            var center = new PixelPoint(region.X + region.Width / 2, region.Y + region.Height / 2);
            var target = source ?? WindowLocator.List().FirstOrDefault(window => window.Client.Contains(center));
            if (target is null || region.Intersect(target.Client) != region)
                return new(null, "The capture region must remain inside the selected window.");
            X11ErrorTrap.Initialize();
            var display = XOpenDisplay(0);
            if (display == 0) return new(null, "The X11 display is unavailable.");
            X11ErrorTrap.Register(display);
            try
            {
                if (XTestQueryExtension(display, out _, out _, out _, out _) == 0)
                    return new(null, "The X11 server does not support the XTEST extension.");
                var process = ReadProcess(display, target.Handle);
                if (process == 0) return new(null, "The selected window process is unavailable.");
                var result = new X11AutomaticScrollInput(display, target, process);
                display = 0;
                return new(result);
            }
            finally
            {
                if (display != 0) CloseDisplay(display);
            }
        }

        private AutomaticScrollInputResult Unavailable(string reason)
        {
            UnavailableReason = reason;
            return AutomaticScrollInputResult.Unavailable;
        }

        private static void CloseDisplay(nint display)
        {
            _ = XSync(display, 0);
            X11ErrorTrap.Unregister(display);
            _ = XCloseDisplay(display);
        }

        private static uint ReadProcess(nint display, nint window)
        {
            var atom = XInternAtom(display, "_NET_WM_PID", 1);
            if (atom == 0) return 0;
            if (XGetWindowProperty(display, window, atom, 0, 1, 0, 6,
                    out var type, out var format, out var count, out _, out var data) != 0) return 0;
            try
            {
                return type == 6 && format == 32 && count == 1 && data != 0
                    ? unchecked((uint)Marshal.ReadInt32(data))
                    : 0;
            }
            finally
            {
                if (data != 0) _ = XFree(data);
            }
        }
    }
}

using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal sealed partial class X11Capture
    {
        internal static bool SetPopupTransient(nint popup, nint parent)
        {
            X11ErrorTrap.Initialize();
            var display = XOpenDisplay(0);
            if (display == 0) return false;
            X11ErrorTrap.Register(display);
            try
            {
                X11ErrorTrap.Clear(display);
                _ = XSetTransientForHint(display, (nuint)popup, (nuint)parent);
                _ = XSync(display, 0);
                return !X11ErrorTrap.HasError(display);
            }
            finally
            {
                X11ErrorTrap.Unregister(display);
                _ = XCloseDisplay(display);
            }
        }

        [LibraryImport("libX11.so.6")]
        private static partial int XSetTransientForHint(nint display, nuint window, nuint parent);
    }
}

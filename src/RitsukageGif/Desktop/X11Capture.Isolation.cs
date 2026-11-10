namespace RitsukageGif.Desktop
{
    internal sealed partial class X11Capture
    {
        internal bool HasIsolatedWindow()
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_display == 0, this);
                if (_window == 0) return false;
                X11ErrorTrap.Clear(_display);
                if (XGetWindowAttributes(_display, _window, out var attributes) == 0 || attributes.MapState != 2)
                {
                    _ = XSync(_display, 0);
                    return false;
                }

                var pixmap = XCompositeNameWindowPixmap(_display, _window);
                _ = XSync(_display, 0);
                try
                {
                    return pixmap != 0 && !X11ErrorTrap.HasError(_display);
                }
                finally
                {
                    if (pixmap != 0) _ = XFreePixmap(_display, pixmap);
                    _ = XSync(_display, 0);
                }
            }
        }
    }
}

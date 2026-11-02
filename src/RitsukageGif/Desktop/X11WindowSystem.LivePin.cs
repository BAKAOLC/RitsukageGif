namespace RitsukageGif.Desktop
{
    internal static partial class X11WindowSystem
    {
        internal static (bool Exists, bool Visible, uint ProcessId) LiveWindowState(nint window)
        {
            lock (Sync)
            {
                var display = Display();
                if (XGetWindowAttributes(display, window, out var attributes) == 0) return (false, false, 0);
                var data = Property(display, window, "_NET_WM_PID", out var format);
                var pid = format == 32 && data.Length >= IntPtr.Size ? BitConverter.ToUInt32(data) : 0;
                return (true, attributes.MapState == 2, pid);
            }
        }
    }
}

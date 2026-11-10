namespace RitsukageGif.Desktop
{
    internal static partial class X11WindowSystem
    {
        internal static nint ActiveWindowHandle()
        {
            lock (Sync)
            {
                var display = Display();
                var value = Property(display, XDefaultRootWindow(display), "_NET_ACTIVE_WINDOW", out var format);
                if (format != 32 || value.Length < IntPtr.Size) return 0;
                return IntPtr.Size == 8 ? (nint)BitConverter.ToInt64(value) : BitConverter.ToInt32(value);
            }
        }
    }
}

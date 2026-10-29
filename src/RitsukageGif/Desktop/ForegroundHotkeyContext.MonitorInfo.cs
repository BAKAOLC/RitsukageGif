using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal sealed partial record ForegroundHotkeyContext
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public uint Size;
            public NativeMethods.Rect Monitor;
            public NativeMethods.Rect Work;
            public uint Flags;
        }
    }
}

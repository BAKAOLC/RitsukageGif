using System;
using System.Runtime.InteropServices;

namespace CaptureGif.Native
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct IconInfo
    {
        public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }
}

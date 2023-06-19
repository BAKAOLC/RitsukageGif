using System;
using System.Runtime.InteropServices;

namespace RitsukageGif.Native
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
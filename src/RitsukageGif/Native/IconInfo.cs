using System;
using System.Runtime.InteropServices;

namespace RitsukageGif.Native
{
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct IconInfo
    {
        public readonly int fIcon;
        public readonly int xHotspot;
        public readonly int yHotspot;
        public readonly IntPtr hbmMask;
        public readonly IntPtr hbmColor;
    }
}
using System;
using System.Runtime.InteropServices;

namespace RitsukageGif.Native
{
    [Serializable, StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public Rect(int dimension)
        {
            Left = Top = Right = Bottom = dimension;
        }
    }
}
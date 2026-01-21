using System;
using System.Runtime.InteropServices;

namespace RitsukageGif.Native
{
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct RectStruct
    {
        public readonly int Left;
        public readonly int Top;
        public readonly int Right;
        public readonly int Bottom;

        public RectStruct(int dimension)
        {
            Left = Top = Right = Bottom = dimension;
        }
    }
}
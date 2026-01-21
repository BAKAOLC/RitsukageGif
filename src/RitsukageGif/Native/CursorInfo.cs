using System;
using System.Runtime.InteropServices;

namespace RitsukageGif.Native
{
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct CursorInfo(int flags, IntPtr hCursor, PointStruct ptScreenPos)
    {
        public readonly int cbSize = Marshal.SizeOf<CursorInfo>();
        public readonly int flags = flags;
        public readonly IntPtr hCursor = hCursor;
        public readonly PointStruct ptScreenPos = ptScreenPos;

        public CursorInfo() : this(0, IntPtr.Zero, default)
        {
        }
    }
}
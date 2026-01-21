using System.Runtime.InteropServices;

namespace RitsukageGif.Native
{
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct PointStruct(int x, int y)
    {
        public readonly int X = x;
        public readonly int Y = y;
    }
}
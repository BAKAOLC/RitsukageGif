using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct HdrSwapChainDescription
    {
        public uint Width, Height;
        public int Format, Stereo;
        public uint SampleCount, SampleQuality, BufferUsage, BufferCount;
        public int Scaling, SwapEffect, AlphaMode;
        public uint Flags;
    }
}

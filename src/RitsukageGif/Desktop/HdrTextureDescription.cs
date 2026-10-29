using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct HdrTextureDescription
    {
        public uint Width,
            Height,
            MipLevels,
            ArraySize,
            Format,
            SampleCount,
            SampleQuality,
            Usage,
            BindFlags,
            CpuAccessFlags,
            MiscFlags;
    }
}

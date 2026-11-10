using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct RecordingMachTimebase
    {
        internal uint Numerator;
        internal uint Denominator;
    }
}

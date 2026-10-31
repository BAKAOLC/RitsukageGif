using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal static partial class CredentialVault
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob
        {
            internal uint Size;
            internal nint Data;
        }
    }
}

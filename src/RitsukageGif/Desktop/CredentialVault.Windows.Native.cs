using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal static partial class CredentialVault
    {
        [LibraryImport("crypt32.dll", SetLastError = true)]
        private static partial int CryptProtectData(ref DataBlob input, nint description, ref DataBlob entropy,
            nint reserved, nint prompt, uint flags, out DataBlob output);

        [LibraryImport("crypt32.dll", SetLastError = true)]
        private static partial int CryptUnprotectData(ref DataBlob input, nint description, ref DataBlob entropy,
            nint reserved, nint prompt, uint flags, out DataBlob output);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        private static partial nint LocalFree(nint memory);
    }
}

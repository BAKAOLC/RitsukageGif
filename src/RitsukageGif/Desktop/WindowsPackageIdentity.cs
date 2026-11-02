using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal static partial class WindowsPackageIdentity
    {
        internal static bool HasIdentity()
        {
            if (!OperatingSystem.IsWindows()) return false;
            uint length = 0;
            if (GetCurrentPackageFullName(ref length, 0) != 122) return false;
            var name = Marshal.AllocHGlobal((int)length * sizeof(char));
            try
            {
                return GetCurrentPackageFullName(ref length, name) == 0;
            }
            finally
            {
                Marshal.FreeHGlobal(name);
            }
        }

        [LibraryImport("kernel32.dll")]
        private static partial int GetCurrentPackageFullName(ref uint length, nint name);
    }
}

using System;
using System.Runtime.InteropServices;

namespace RitsukageGif.Native
{
    internal partial class Shcore
    {
        private const string DllName = "shcore.dll";

        [LibraryImport(DllName)]
        public static partial int GetDpiForMonitor(IntPtr hMonitor, DpiType dpiType, out uint dpiX, out uint dpiY);
    }
}
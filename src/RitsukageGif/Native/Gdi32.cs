using System;
using System.Runtime.InteropServices;

namespace RitsukageGif.Native
{
    internal partial class Gdi32
    {
        private const string DllName = "gdi32.dll";

        [LibraryImport(DllName)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool DeleteObject(IntPtr hObject);
    }
}
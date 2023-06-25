using System;
using System.Runtime.InteropServices;

namespace RitsukageGif.Native
{
    internal class Gdi32
    {
        private const string DllName = "gdi32.dll";

        [DllImport(DllName)]
        public static extern bool DeleteObject(IntPtr hObject);
    }
}
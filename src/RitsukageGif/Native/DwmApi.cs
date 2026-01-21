using System;
using System.Runtime.InteropServices;

namespace RitsukageGif.Native
{
    internal partial class DwmApi
    {
        private const string DllName = "dwmapi.dll";

        [LibraryImport(DllName, EntryPoint = "DwmGetWindowAttribute")]
        private static partial int DwmGetWindowAttribute_Bool(IntPtr window, int attribute, out int value, int size);

        public static int DwmGetWindowAttribute(IntPtr window, int attribute, out bool value)
        {
            var result = DwmGetWindowAttribute_Bool(window, attribute, out var intValue, Marshal.SizeOf<bool>());
            value = intValue != 0;
            return result;
        }

        [LibraryImport(DllName, EntryPoint = "DwmGetWindowAttribute")]
        private static partial int DwmGetWindowAttribute_Rect(IntPtr window, int attribute, ref RectStruct value,
            int size);

        public static int DwmGetWindowAttribute(IntPtr window, int attribute, ref RectStruct value)
        {
            return DwmGetWindowAttribute_Rect(window, attribute, ref value, Marshal.SizeOf<RectStruct>());
        }
    }
}
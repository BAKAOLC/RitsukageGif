using System;

namespace RitsukageGif.Native
{
    [Flags]
    internal enum KeyModifiers
    {
        None = 0,
        Alt = 1,
        Ctrl = 2,
        Shift = 4,
        WindowsKey = 8
    }
}

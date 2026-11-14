using System;

namespace Avalonia.Win32
{
    internal class OffscreenParentWindow
    {
        private static readonly SimpleWindow s_simpleWindow = new(null);
        public static IntPtr Handle { get; } = s_simpleWindow.Handle;
    }
}

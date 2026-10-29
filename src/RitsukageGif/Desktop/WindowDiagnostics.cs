using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    internal static partial class WindowDiagnostics
    {
        internal static void Write(ILogger logger, string stage, Window window)
        {
            if (!logger.IsEnabled(LogLevel.Debug)) return;
            var native = window.TryGetPlatformHandle() is { HandleDescriptor: "HWND" } handle ? handle.Handle : 0;
            var visible = false;
            var cloakResult = 0;
            uint cloak = 0;
            var style = 0;
            PixelRect rectangle = default;
            if (OperatingSystem.IsWindows() && native != 0)
            {
                visible = IsWindowVisible(native);
                style = GetWindowLong(native, -20);
                cloakResult = DwmGetWindowAttribute(native, 14, out cloak, sizeof(uint));
                if (NativeMethods.GetWindowRect(native, out var bounds) != 0) rectangle = bounds.Pixels;
            }

            logger.ZLogDebug(
                $"Window state. Stage={stage}; Type={window.GetType().Name}; Handle={native}; Visible={window.IsVisible}; NativeVisible={visible}; Enabled={window.IsEnabled}; Active={window.IsActive}; Topmost={window.Topmost}; Bounds={rectangle}; ExtendedStyle={style}; CloakResult={cloakResult}; Cloak={cloak}");
        }

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool IsWindowVisible(nint window);

        [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static partial int GetWindowLong(nint window, int index);

        [LibraryImport("dwmapi.dll")]
        private static partial int DwmGetWindowAttribute(nint window, uint attribute, out uint value, uint size);
    }
}

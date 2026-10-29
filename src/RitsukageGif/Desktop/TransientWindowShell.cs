using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    internal static partial class TransientWindowShell
    {
        private const uint AppWindow = 0x00040000;
        private const uint ToolWindow = 0x00000080;

        private static readonly AttachedProperty<bool> ConfiguredProperty =
            AvaloniaProperty.RegisterAttached<Window, bool>("TransientShellConfigured", typeof(TransientWindowShell));

        internal static void Configure(Window window)
        {
            if (window.GetValue(ConfiguredProperty)) return;
            if (OperatingSystem.IsWindows()) Win32Properties.AddWindowStylesCallback(window, FilterStyles);
            try
            {
                window.ShowInTaskbar = false;
                if (OperatingSystem.IsWindows() &&
                    window.TryGetPlatformHandle() is { HandleDescriptor: "HWND" } handle)
                    ApplyNativeStyles(handle.Handle);
            }
            catch
            {
                if (OperatingSystem.IsWindows()) Win32Properties.RemoveWindowStylesCallback(window, FilterStyles);
                throw;
            }

            window.SetValue(ConfiguredProperty, true);
            window.Closed += Closed;
        }

        private static (uint Style, uint ExtendedStyle) FilterStyles(uint style, uint extendedStyle)
        {
            return (style, (extendedStyle | ToolWindow) & ~AppWindow);
        }

        private static void Closed(object? sender, EventArgs args)
        {
            if (sender is not Window window) return;
            window.Closed -= Closed;
            if (OperatingSystem.IsWindows()) Win32Properties.RemoveWindowStylesCallback(window, FilterStyles);
            window.ClearValue(ConfiguredProperty);
        }

        private static void ApplyNativeStyles(nint window)
        {
            Marshal.SetLastPInvokeError(0);
            var oldStyle = GetWindowLong(window, -20);
            if (oldStyle == 0 && Marshal.GetLastPInvokeError() != 0)
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            var style = (oldStyle | (int)ToolWindow) & ~(int)AppWindow;
            if (style == oldStyle) return;
            Marshal.SetLastPInvokeError(0);
            if ((SetWindowLong(window, -20, style) == 0 && Marshal.GetLastPInvokeError() != 0) ||
                !SetWindowPos(window, 0, 0, 0, 0, 0, 0x0237))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
        private static partial int GetWindowLong(nint window, int index);

        [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        private static partial int SetWindowLong(nint window, int index, int value);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetWindowPos(nint window, nint after, int x, int y, int width, int height,
            uint flags);
    }
}

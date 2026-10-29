using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    internal static partial class WindowActivation
    {
        internal static void Show(Window window)
        {
            window.Show();
            Restore(window);
            var target = ResolveTarget(window);
            if (target != window) Restore(target);
            target.Activate();
            if (OperatingSystem.IsWindows() && target.TryGetPlatformHandle() is { HandleDescriptor: "HWND" } handle &&
                SetForegroundWindow(handle.Handle) == 0 && GetForegroundWindow() != handle.Handle)
                Trace.TraceInformation("Windows did not grant foreground activation to the requested window.");
        }

        private static Window ResolveTarget(Window window)
        {
            while (window.OwnedWindows.LastOrDefault(candidate => candidate is { IsVisible: true, ShowActivated: true })
                   is { } child)
                window = child;
            return window;
        }

        private static void Restore(Window window)
        {
            if (window.WindowState != WindowState.Minimized) return;
            if (OperatingSystem.IsWindows() && window.TryGetPlatformHandle() is { HandleDescriptor: "HWND" } handle &&
                NativeMethods.IsIconic(handle.Handle) != 0)
            {
#pragma warning disable CA1806
                ShowWindow(handle.Handle, 9); // SW_RESTORE returns prior visibility and preserves maximized state.
#pragma warning restore CA1806
                if (NativeMethods.IsIconic(handle.Handle) != 0) window.WindowState = WindowState.Normal;
            }
            else
            {
                window.WindowState = WindowState.Normal;
            }
        }

        [LibraryImport("user32.dll")]
        // ReSharper disable once UnusedMethodReturnValue.Local -- Keep the native ABI; Windows may deny activation.
        private static partial int SetForegroundWindow(nint window);

        [LibraryImport("user32.dll")]
        private static partial nint GetForegroundWindow();

        [LibraryImport("user32.dll")]
        // ReSharper disable once UnusedMethodReturnValue.Local -- The native return value describes prior visibility.
        private static partial int ShowWindow(nint window, int command);
    }
}

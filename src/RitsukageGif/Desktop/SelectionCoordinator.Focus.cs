using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SelectionCoordinator
    {
        private static bool IsForeignCaptureWindowActivated()
        {
            if (!OperatingSystem.IsWindows())
                return Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime
                           lifetime ||
                       !lifetime.Windows.Any(window => window.IsActive);
            return IsForeignCaptureWindow(CaptureForegroundWindow());
        }

        private static bool IsForeignCaptureWindow(nint window)
        {
            return window != 0 && CaptureWindowProcess(window, out var process) != 0 &&
                   process != Environment.ProcessId;
        }

        [LibraryImport("user32.dll", EntryPoint = "GetForegroundWindow")]
        private static partial nint CaptureForegroundWindow();

        [LibraryImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")]
        private static partial uint CaptureWindowProcess(nint window, out uint process);
    }
}

using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal static partial class WindowLocator
    {
        internal static bool CanFindActiveWindow => !DesktopCapture.UsesSourceSelection &&
                                                    (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ||
                                                     OperatingSystem.IsLinux());

        internal static async Task<WindowTarget?> ActiveWindowAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!CanFindActiveWindow)
                throw new PlatformNotSupportedException(UiText.Get("GlobalActionActiveWindowUnavailable"));
            var handle = OperatingSystem.IsWindows() ? GetActiveWindowHandle() :
                OperatingSystem.IsMacOS() ? await MacWindowSystem.ActiveWindowHandleAsync(token).ConfigureAwait(false) :
                await Task.Run(X11WindowSystem.ActiveWindowHandle, token).ConfigureAwait(false);
            if (handle == 0) return null;
            var candidates = await Task.Run(ListCandidates, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return candidates.FirstOrDefault(window => window.Handle == handle);
        }

        [LibraryImport("user32.dll", EntryPoint = "GetForegroundWindow")]
        private static partial nint GetActiveWindowHandle();
    }
}

using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal static partial class CaptureWindowVisibility
    {
        private static readonly ILogger VisibilityLogger = LogHub.GetLogger("Desktop.Capture.Visibility");

        internal static async Task FlushAsync(CancellationToken token)
        {
            Dispatcher.UIThread.VerifyAccess();
            token.ThrowIfCancellationRequested();
            VisibilityLogger.ZLogDebug($"Composition flush requested.");
            if (OperatingSystem.IsWindows())
                await Task.Run(FlushComposition, CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(5), token);
            token.ThrowIfCancellationRequested();
            VisibilityLogger.ZLogDebug($"Composition flush completed.");
        }

        internal static async Task HideAsync(Window? window, CancellationToken token = default)
        {
            Dispatcher.UIThread.VerifyAccess();
            token.ThrowIfCancellationRequested();
            if (window is null) return;
            WindowDiagnostics.Write(VisibilityLogger, "BeforeCaptureHide", window);
            var native = OperatingSystem.IsWindows() && window.TryGetPlatformHandle() is
                { HandleDescriptor: "HWND" } handle
                ? handle.Handle
                : 0;
            var restoreCloak = false;
            try
            {
                if (native != 0)
                {
                    Marshal.ThrowExceptionForHR(DwmIsCompositionEnabled(out var composition));
                    if (composition != 0)
                    {
                        Marshal.ThrowExceptionForHR(DwmGetWindowAttribute(native, 14, out var cloak, sizeof(uint)));
                        if ((cloak & 1) == 0)
                        {
                            var enabled = 1u;
                            Marshal.ThrowExceptionForHR(DwmSetWindowAttribute(native, 13, in enabled, sizeof(uint)));
                            restoreCloak = true;
                            VisibilityLogger.ZLogDebug($"Native capture window cloaked. Handle={native}");
                        }

                        VisibilityLogger.ZLogDebug($"Cloaked window composition flush started. Handle={native}");
                        await Task.Run(FlushComposition, CancellationToken.None)
                            .WaitAsync(TimeSpan.FromSeconds(5), token);
                        VisibilityLogger.ZLogDebug($"Cloaked window composition flush completed. Handle={native}");
                    }
                }

                window.Hide();
                WindowDiagnostics.Write(VisibilityLogger, "AfterCaptureHide", window);
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background, CancellationToken.None);
                if (native != 0)
                {
                    if (IsWindowVisible(native) != 0)
                        throw new InvalidOperationException(UiText.Get("CaptureHideFailed"));
                    await Task.Run(FlushComposition, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5), token);
                }

                token.ThrowIfCancellationRequested();
            }
            finally
            {
                if (restoreCloak && IsWindow(native) != 0)
                {
                    var disabled = 0u;
                    Marshal.ThrowExceptionForHR(DwmSetWindowAttribute(native, 13, in disabled, sizeof(uint)));
                    VisibilityLogger.ZLogDebug($"Native capture window cloak restored. Handle={native}");
                }
            }
        }

        private static void FlushComposition()
        {
            Marshal.ThrowExceptionForHR(DwmIsCompositionEnabled(out var enabled));
            if (enabled != 0) Marshal.ThrowExceptionForHR(DwmFlush());
        }

        [LibraryImport("dwmapi.dll")]
        private static partial int DwmIsCompositionEnabled(out int enabled);

        [LibraryImport("dwmapi.dll")]
        private static partial int DwmGetWindowAttribute(nint window, uint attribute, out uint value, uint size);

        [LibraryImport("dwmapi.dll")]
        private static partial int DwmSetWindowAttribute(nint window, uint attribute, in uint value, uint size);

        [LibraryImport("dwmapi.dll")]
        private static partial int DwmFlush();

        [LibraryImport("user32.dll")]
        private static partial int IsWindowVisible(nint window);

        [LibraryImport("user32.dll")]
        private static partial int IsWindow(nint window);
    }
}

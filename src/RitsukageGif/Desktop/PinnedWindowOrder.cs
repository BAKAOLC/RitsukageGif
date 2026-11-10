using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal static partial class PinnedWindowOrder
    {
        private static readonly ILogger Logger = LogHub.GetLogger("Desktop.Pins.Order");

        internal static void Restore(IReadOnlyList<IPinnedWindow> order)
        {
            var visible = order.Where(window => window.IsVisible).ToArray();
            if (OperatingSystem.IsLinux())
            {
                var handles = visible.Select(window => window.Window.TryGetPlatformHandle())
                    .Where(handle => handle is { HandleDescriptor: "XID", Handle: not 0 })
                    .Select(handle => handle!.Handle).ToArray();
                if (handles.Length != 0) X11WindowSystem.RaiseWithoutActivation(handles);
                else if (visible.Length != 0)
                    Logger.ZLogWarning($"Pin stacking restore unavailable for this window backend.");
                return;
            }

            foreach (var window in visible)
            {
                var handle = window.Window.TryGetPlatformHandle();
                if (handle is null || handle.Handle == 0) continue;
                if (OperatingSystem.IsWindows() && handle.HandleDescriptor == "HWND")
                {
                    if (NativeMethods.SetWindowPos(handle.Handle, window.Window.Topmost ? -1 : 0,
                            0, 0, 0, 0, 0x0013) == 0)
                        Logger.ZLogWarning($"Windows did not restore pin stacking order.");
                }
                else if (OperatingSystem.IsMacOS())
                {
                    var nativeWindow = handle.HandleDescriptor switch
                    {
                        "NSWindow" => handle.Handle,
                        "NSView" => SendPointer(handle.Handle, RegisterSelector("window")),
                        _ => 0,
                    };
                    if (nativeWindow != 0) SendVoid(nativeWindow, RegisterSelector("orderFrontRegardless"));
                    else Logger.ZLogWarning($"Pin stacking restore unavailable for this macOS window handle.");
                }
            }
        }

        [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName",
            StringMarshalling = StringMarshalling.Utf8)]
        private static partial nint RegisterSelector(string name);

        [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static partial nint SendPointer(nint target, nint selector);

        [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static partial void SendVoid(nint target, nint selector);
    }
}

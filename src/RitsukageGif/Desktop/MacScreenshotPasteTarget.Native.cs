using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal sealed partial class MacScreenshotPasteTarget
    {
        private const string ApplicationServices =
            "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";

        private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
        private const string ObjC = "/usr/lib/libobjc.A.dylib";

        [LibraryImport(ApplicationServices, EntryPoint = "AXIsProcessTrusted")]
        [return: MarshalAs(UnmanagedType.U1)]
        private static partial bool AxIsProcessTrusted();

        [LibraryImport(ApplicationServices)]
        private static partial nint AXUIElementCreateSystemWide();

        [LibraryImport(ApplicationServices)]
        private static partial int AXUIElementSetMessagingTimeout(nint element, float seconds);

        [LibraryImport(ApplicationServices, EntryPoint = "AXUIElementCopyAttributeValue")]
        private static partial int AxuiElementCopyAttributeValue(nint element, nint attribute, out nint value);

        [LibraryImport(ApplicationServices, EntryPoint = "AXUIElementGetPid")]
        private static partial int AxuiElementGetPid(nint element, out int processId);

        [LibraryImport(CoreFoundation, StringMarshalling = StringMarshalling.Utf8,
            EntryPoint = "CFStringCreateWithCString")]
        private static partial nint CfStringCreateWithCString(nint allocator, string text, uint encoding);

        [LibraryImport(CoreFoundation)]
        private static partial void CFRelease(nint value);

        [LibraryImport(CoreFoundation, EntryPoint = "CFEqual")]
        [return: MarshalAs(UnmanagedType.U1)]
        private static partial bool CfEqual(nint first, nint second);

        [LibraryImport(CoreGraphics, EntryPoint = "CGPreflightPostEventAccess")]
        [return: MarshalAs(UnmanagedType.U1)]
        private static partial bool CgPreflightPostEventAccess();

        [LibraryImport(CoreGraphics)]
        private static partial ulong CGEventSourceFlagsState(int state);

        [LibraryImport(CoreGraphics, EntryPoint = "CGEventCreateKeyboardEvent")]
        private static partial nint CgEventCreateKeyboardEvent(nint source, ushort key,
            [MarshalAs(UnmanagedType.U1)] bool down);

        [LibraryImport(CoreGraphics)]
        private static partial void CGEventSetFlags(nint keyboardEvent, ulong flags);

        [LibraryImport(CoreGraphics)]
        private static partial void CGEventPostToPid(int processId, nint keyboardEvent);

        [LibraryImport(ObjC, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)]
        private static partial nint GetClass(string name);

        [LibraryImport(ObjC, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
        private static partial nint RegisterSelector(string name);

        [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
        private static partial nint SendPointer(nint target, nint selector);

        [LibraryImport(ObjC, EntryPoint = "objc_autoreleasePoolPush")]
        private static partial nint AutoreleasePoolPush();

        [LibraryImport(ObjC, EntryPoint = "objc_autoreleasePoolPop")]
        private static partial void AutoreleasePoolPop(nint pool);
    }
}

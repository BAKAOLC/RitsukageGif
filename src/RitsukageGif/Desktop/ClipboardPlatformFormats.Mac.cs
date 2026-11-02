using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal static partial class ClipboardPlatformFormats
    {
        private const string CoreServices = "/System/Library/Frameworks/CoreServices.framework/CoreServices";

        private static unsafe string MacNativeFormat(string name)
        {
            var library = NativeLibrary.Load(CoreServices);
            var input = CfStringCreateWithCString(0, name, 0x08000100);
            nint identifier = 0;
            try
            {
                var tagClass = Marshal.ReadIntPtr(NativeLibrary.GetExport(library,
                    name.Contains('/') ? "kUTTagClassMIMEType" : "kUTTagClassNSPboardType"));
                identifier = UTTypeCreatePreferredIdentifierForTag(tagClass, input, 0);
                if (identifier == 0)
                    throw new NotSupportedException("The clipboard format has no native type identifier.");
                var capacity =
                    checked((int)CFStringGetMaximumSizeForEncoding(CFStringGetLength(identifier), 0x08000100) + 1);
                var buffer = new byte[capacity];
                fixed (byte* pointer = buffer)
                {
                    if (CFStringGetCString(identifier, pointer, capacity, 0x08000100) == 0)
                        throw new NotSupportedException("The native clipboard type identifier could not be read.");
                    return Marshal.PtrToStringUTF8((nint)pointer)!;
                }
            }
            finally
            {
                if (identifier != 0) CFRelease(identifier);
                CFRelease(input);
                NativeLibrary.Free(library);
            }
        }

        [LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation",
            EntryPoint = "CFStringCreateWithCString", StringMarshalling = StringMarshalling.Utf8)]
        private static partial nint CfStringCreateWithCString(nint allocator, string text, uint encoding);

        [LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
        private static partial void CFRelease(nint value);

        [LibraryImport(CoreServices)]
        private static partial nint UTTypeCreatePreferredIdentifierForTag(nint tagClass, nint tag, nint conformingTo);

        [LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
        private static partial nint CFStringGetLength(nint value);

        [LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
        private static partial nint CFStringGetMaximumSizeForEncoding(nint length, uint encoding);

        [LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
        private static unsafe partial byte CFStringGetCString(nint value, byte* buffer, nint size, uint encoding);
    }
}

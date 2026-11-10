using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace RitsukageGif.Desktop
{
    internal static partial class PinnedFileIconProvider
    {
        private const nuint MacPngFileType = 4;

        [SupportedOSPlatform("macos")]
        private static byte[] ReadMac(string path, int size, CancellationToken token)
        {
            var appKit = NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit");
            nint pool = 0;
            nint bitmap = 0;
            try
            {
                pool = Send(Send(ObjcClass("NSAutoreleasePool"), Selector("alloc")), Selector("init"));
                token.ThrowIfCancellationRequested();
                var workspace = Send(ObjcClass("NSWorkspace"), Selector("sharedWorkspace"));
                var file = SendUtf8(ObjcClass("NSString"), Selector("stringWithUTF8String:"), path);
                var image = SendArgument(workspace, Selector("iconForFile:"), file);
                if (image == 0) throw new IOException("NSWorkspace returned no file icon.");
                var rect = new FileIconMacRect { Width = size, Height = size };
                var cgImage = SendImage(image, Selector("CGImageForProposedRect:context:hints:"), ref rect, 0, 0);
                if (cgImage == 0) throw new IOException("The NSWorkspace icon could not be rasterized.");
                bitmap = SendArgument(Send(ObjcClass("NSBitmapImageRep"), Selector("alloc")),
                    Selector("initWithCGImage:"), cgImage);
                if (bitmap == 0) throw new IOException("The NSWorkspace icon bitmap could not be created.");
                var properties = Send(ObjcClass("NSDictionary"), Selector("dictionary"));
                var data = SendRepresentation(bitmap, Selector("representationUsingType:properties:"), MacPngFileType,
                    properties);
                if (data == 0) throw new IOException("The NSWorkspace icon could not be encoded as PNG.");
                var length = SendLength(data, Selector("length"));
                if (length is 0 or > 4 * 1024 * 1024)
                    throw new IOException("The NSWorkspace icon has an invalid encoded size.");
                var bytes = new byte[(int)length];
                Marshal.Copy(Send(data, Selector("bytes")), bytes, 0, bytes.Length);
                return bytes;
            }
            finally
            {
                if (bitmap != 0) SendVoid(bitmap, Selector("release"));
                if (pool != 0) SendVoid(pool, Selector("drain"));
                NativeLibrary.Free(appKit);
            }
        }

        [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_getClass",
            StringMarshalling = StringMarshalling.Utf8)]
        private static partial nint ObjcClass(string name);

        [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName",
            StringMarshalling = StringMarshalling.Utf8)]
        private static partial nint Selector(string name);

        [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static partial nint Send(nint receiver, nint selector);

        [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static partial void SendVoid(nint receiver, nint selector);

        [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend",
            StringMarshalling = StringMarshalling.Utf8)]
        private static partial nint SendUtf8(nint receiver, nint selector, string argument);

        [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static partial nint SendArgument(nint receiver, nint selector, nint argument);

        [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static partial nint SendImage(nint receiver, nint selector, ref FileIconMacRect rect, nint context,
            nint hints);

        [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static partial nint SendRepresentation(nint receiver, nint selector, nuint type, nint properties);

        [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static partial nuint SendLength(nint receiver, nint selector);
    }
}

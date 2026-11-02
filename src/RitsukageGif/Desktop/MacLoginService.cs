using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace RitsukageGif.Desktop
{
    [SupportedOSPlatform("macos13.0")]
    internal sealed partial class MacLoginService : IMacLoginService
    {
        private static readonly nint ServiceManagement = NativeLibrary.Load(
            "/System/Library/Frameworks/ServiceManagement.framework/ServiceManagement");

        public StartupRegistrationStatus Status => Invoke(null);

        public void SetEnabled(bool enabled)
        {
            _ = Invoke(enabled);
        }

        private static StartupRegistrationStatus Invoke(bool? enabled)
        {
            _ = ServiceManagement;
            var pool = Send(Send(GetClass("NSAutoreleasePool"), Selector("alloc")), Selector("init"));
            try
            {
                var name = SendUtf8(GetClass("NSString"), Selector("stringWithUTF8String:"),
                    MacServiceStartupRegistration.PlistName);
                var service = SendArgument(GetClass("SMAppService"), Selector("agentServiceWithPlistName:"), name);
                if (service == 0) throw new InvalidOperationException("The bundled login agent could not be opened.");
                nint error = 0;
                var succeeded = enabled is not { } requested || SendError(service,
                    Selector(requested ? "registerAndReturnError:" : "unregisterAndReturnError:"), out error);
                var status = SendInteger(service, Selector("status"));
                if (!succeeded && !((enabled == true && status is 1 or 2) || (enabled == false && status == 0)))
                {
                    var description = error == 0 ? 0 : Send(error, Selector("localizedDescription"));
                    var utf8 = description == 0 ? 0 : Send(description, Selector("UTF8String"));
                    throw new InvalidOperationException("Login item registration failed: " +
                                                        (utf8 == 0
                                                            ? "Unknown ServiceManagement error."
                                                            : Marshal.PtrToStringUTF8(utf8)));
                }

                return status switch
                {
                    0 => StartupRegistrationStatus.Disabled,
                    1 => StartupRegistrationStatus.Enabled,
                    2 => StartupRegistrationStatus.ApprovalRequired,
                    _ => throw new InvalidOperationException(
                        "The bundled login agent was not found by ServiceManagement."),
                };
            }
            finally
            {
                SendVoid(pool, Selector("drain"));
            }
        }

        [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_getClass",
            StringMarshalling = StringMarshalling.Utf8)]
        private static partial nint GetClass(string name);

        [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName",
            StringMarshalling = StringMarshalling.Utf8)]
        private static partial nint Selector(string name);

        [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static partial nint Send(nint receiver, nint selector);

        [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static partial void SendVoid(nint receiver, nint selector);

        [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static partial nint SendInteger(nint receiver, nint selector);

        [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend",
            StringMarshalling = StringMarshalling.Utf8)]
        private static partial nint SendUtf8(nint receiver, nint selector, string argument);

        [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static partial nint SendArgument(nint receiver, nint selector, nint argument);

        [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        [return: MarshalAs(UnmanagedType.I1)]
        private static partial bool SendError(nint receiver, nint selector, out nint error);
    }
}

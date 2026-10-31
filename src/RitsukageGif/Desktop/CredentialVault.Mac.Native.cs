using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal static partial class CredentialVault
    {
        [LibraryImport(SecurityFramework)]
        private static partial int SecItemCopyMatching(nint query, out nint result);

        [LibraryImport(SecurityFramework)]
        private static partial int SecItemAdd(nint attributes, nint result);

        [LibraryImport(SecurityFramework)]
        private static partial int SecItemUpdate(nint query, nint changes);

        [LibraryImport(SecurityFramework)]
        private static partial int SecItemDelete(nint query);

        [LibraryImport(CoreFoundationFramework)]
        private static partial nint CFDictionaryCreateMutable(nint allocator, nint capacity, nint keys, nint values);

        [LibraryImport(CoreFoundationFramework)]
        private static partial void CFDictionarySetValue(nint dictionary, nint key, nint value);

        [LibraryImport(CoreFoundationFramework)]
        private static partial void CFDictionaryRemoveValue(nint dictionary, nint key);

        [LibraryImport(CoreFoundationFramework)]
        private static partial nint CFStringCreateWithCharacters(nint allocator, nint characters, nint length);

        [LibraryImport(CoreFoundationFramework)]
        private static partial nint CFDataCreate(nint allocator, nint bytes, nint length);

        [LibraryImport(CoreFoundationFramework)]
        private static partial nint CFDataGetLength(nint data);

        [LibraryImport(CoreFoundationFramework)]
        private static partial nint CFDataGetBytePtr(nint data);

        [LibraryImport(CoreFoundationFramework)]
        private static partial void CFRelease(nint value);
    }
}

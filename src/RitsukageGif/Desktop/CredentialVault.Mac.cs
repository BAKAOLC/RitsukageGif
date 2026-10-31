using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace RitsukageGif.Desktop
{
    internal static partial class CredentialVault
    {
        private const int MacItemNotFound = -25300;
        private const int MacDuplicateItem = -25299;
        private const string SecurityFramework = "/System/Library/Frameworks/Security.framework/Security";

        private const string CoreFoundationFramework =
            "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

        private static readonly Lazy<nint> MacSecurity = new(() => NativeLibrary.Load(SecurityFramework));
        private static readonly Lazy<nint> MacFoundation = new(() => NativeLibrary.Load(CoreFoundationFramework));

        private static string? ReadMac(string key)
        {
            var query = CreateMacQuery(key);
            nint result = 0;
            try
            {
                CFDictionarySetValue(query, MacSecurityConstant("kSecReturnData"),
                    MacFoundationConstant("kCFBooleanTrue"));
                var status = SecItemCopyMatching(query, out result);
                if (status == MacItemNotFound) return null;
                RequireMacSuccess(status);
                var length = CFDataGetLength(result);
                if (length is < 0 or > MaximumSecretBytes)
                    throw new IOException("The macOS credential entry exceeds the vault size limit.");
                var bytes = new byte[(int)length];
                try
                {
                    Marshal.Copy(CFDataGetBytePtr(result), bytes, 0, bytes.Length);
                    return SecretEncoding.GetString(bytes);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(bytes);
                }
            }
            finally
            {
                if (result != 0) CFRelease(result);
                CFRelease(query);
            }
        }

        private static unsafe void WriteMac(string key, string secret)
        {
            var bytes = SecretEncoding.GetBytes(secret);
            nint query = 0, changes = 0, data = 0;
            try
            {
                query = CreateMacQuery(key);
                changes = CreateMacDictionary();
                fixed (byte* pointer = bytes)
                    data = CFDataCreate(0, (nint)pointer, bytes.Length);
                if (data == 0) throw new OutOfMemoryException();
                CFDictionarySetValue(changes, MacSecurityConstant("kSecValueData"), data);
                var status = SecItemUpdate(query, changes);
                if (status == MacItemNotFound)
                {
                    CFDictionarySetValue(query, MacSecurityConstant("kSecValueData"), data);
                    status = SecItemAdd(query, 0);
                    if (status == MacDuplicateItem)
                    {
                        CFDictionaryRemoveValue(query, MacSecurityConstant("kSecValueData"));
                        status = SecItemUpdate(query, changes);
                    }
                }

                RequireMacSuccess(status);
            }
            finally
            {
                if (changes != 0) CFRelease(changes);
                if (query != 0) CFRelease(query);
                if (data != 0) CFRelease(data);
                CryptographicOperations.ZeroMemory(bytes);
            }
        }

        private static void DeleteMac(string key)
        {
            var query = CreateMacQuery(key);
            try
            {
                var status = SecItemDelete(query);
                if (status != MacItemNotFound) RequireMacSuccess(status);
            }
            finally
            {
                CFRelease(query);
            }
        }

        private static nint CreateMacQuery(string key)
        {
            var query = CreateMacDictionary();
            try
            {
                CFDictionarySetValue(query, MacSecurityConstant("kSecClass"),
                    MacSecurityConstant("kSecClassGenericPassword"));
                CFDictionarySetValue(query, MacSecurityConstant("kSecAttrSynchronizable"),
                    MacFoundationConstant("kCFBooleanFalse"));
                CFDictionarySetValue(query, MacSecurityConstant("kSecUseAuthenticationUI"),
                    MacSecurityConstant("kSecUseAuthenticationUIFail"));
                SetMacString(query, "kSecAttrService", Service + "." + Profile);
                SetMacString(query, "kSecAttrAccount", KeyHash(key));
                return query;
            }
            catch
            {
                CFRelease(query);
                throw;
            }
        }

        private static nint CreateMacDictionary()
        {
            var result = CFDictionaryCreateMutable(0, 0,
                NativeLibrary.GetExport(MacFoundation.Value, "kCFTypeDictionaryKeyCallBacks"),
                NativeLibrary.GetExport(MacFoundation.Value, "kCFTypeDictionaryValueCallBacks"));
            return result != 0 ? result : throw new OutOfMemoryException();
        }

        private static unsafe void SetMacString(nint dictionary, string name, string value)
        {
            nint text;
            fixed (char* pointer = value) text = CFStringCreateWithCharacters(0, (nint)pointer, value.Length);
            if (text == 0) throw new OutOfMemoryException();
            try
            {
                CFDictionarySetValue(dictionary, MacSecurityConstant(name), text);
            }
            finally
            {
                CFRelease(text);
            }
        }

        private static nint MacSecurityConstant(string name)
        {
            return Marshal.ReadIntPtr(NativeLibrary.GetExport(MacSecurity.Value, name));
        }

        private static nint MacFoundationConstant(string name)
        {
            return Marshal.ReadIntPtr(NativeLibrary.GetExport(MacFoundation.Value, name));
        }

        private static void RequireMacSuccess(int status)
        {
            if (status != 0)
                throw new IOException("The macOS Keychain is unavailable, locked, or denied access. Status=" + status);
        }
    }
}

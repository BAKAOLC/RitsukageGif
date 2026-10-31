using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace RitsukageGif.Desktop
{
    internal static partial class CredentialVault
    {
        private static string WindowsDirectory =>
            Path.GetFullPath(Path.Combine(ApplicationStorage.DataDirectory, "credentials"));

        private static string WindowsPath(string key)
        {
            return Path.Combine(WindowsDirectory, KeyHash(key) + ".dpapi");
        }

        private static async Task<string?> ReadWindowsAsync(string key, CancellationToken cancellationToken)
        {
            var path = WindowsPath(key);
            ValidateWindowsPath(path);
            byte[] encrypted;
            try
            {
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.Read | FileShare.Delete);
                if (stream.Length > MaximumSecretBytes + 65536)
                    throw new IOException("The credential vault entry exceeds the allowed size.");
                encrypted = new byte[checked((int)stream.Length)];
                await stream.ReadExactlyAsync(encrypted, cancellationToken).ConfigureAwait(false);
            }
            catch (FileNotFoundException)
            {
                return null;
            }
            catch (DirectoryNotFoundException)
            {
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var plain = await Task.Run(() => TransformWindows(encrypted, key, false), cancellationToken)
                .ConfigureAwait(false);
            try
            {
                return SecretEncoding.GetString(plain);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }

        private static async Task WriteWindowsAsync(string key, string secret, CancellationToken cancellationToken)
        {
            var plain = SecretEncoding.GetBytes(secret);
            byte[] encrypted;
            try
            {
                encrypted = await Task.Run(() => TransformWindows(plain, key, true), cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var path = WindowsPath(key);
            ValidateWindowsPath(path);
            Directory.CreateDirectory(WindowsDirectory);
            ValidateWindowsPath(path);
            var temporary = Path.Combine(WindowsDirectory, KeyHash(key) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                await File.WriteAllBytesAsync(temporary, encrypted, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporary, path, true);
            }
            finally
            {
                DeleteWindowsFile(temporary);
            }
        }

        private static void DeleteWindows(string key)
        {
            DeleteWindowsFile(WindowsPath(key));
        }

        private static void DeleteWindowsFile(string path)
        {
            ValidateWindowsPath(path);
            try
            {
                File.Delete(path);
            }
            catch (DirectoryNotFoundException)
            {
            }
        }

        private static void ValidateWindowsPath(string path)
        {
            var root = WindowsDirectory;
            var resolved = Path.GetFullPath(path);
            if (!resolved.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetDirectoryName(resolved), root, StringComparison.OrdinalIgnoreCase))
                throw new IOException("The credential entry is outside its vault directory.");
            for (var directory = new DirectoryInfo(root); directory is not null; directory = directory.Parent)
                if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("The credential vault cannot use redirected directories.");
            if (File.Exists(resolved) && (File.GetAttributes(resolved) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The credential vault cannot use redirected entries.");
        }

        private static unsafe byte[] TransformWindows(byte[] input, string key, bool protect)
        {
            var entropy = Encoding.UTF8.GetBytes(Service + "\0" + Profile + "\0" + key);
            fixed (byte* inputPointer = input)
            fixed (byte* entropyPointer = entropy)
            {
                byte empty = 0;
                var source = new DataBlob
                    { Size = checked((uint)input.Length), Data = (nint)(input.Length == 0 ? &empty : inputPointer) };
                var purpose = new DataBlob { Size = checked((uint)entropy.Length), Data = (nint)entropyPointer };
                var success = protect
                    ? CryptProtectData(ref source, 0, ref purpose, 0, 0, 1, out var result)
                    : CryptUnprotectData(ref source, 0, ref purpose, 0, 0, 1, out result);
                if (success == 0)
                    throw new Win32Exception(Marshal.GetLastPInvokeError(),
                        "Windows could not access the encrypted credential vault.");
                byte[] output;
                nint remaining;
                try
                {
                    output = new ReadOnlySpan<byte>((void*)result.Data, checked((int)result.Size)).ToArray();
                }
                finally
                {
                    if (!protect)
                        CryptographicOperations.ZeroMemory(new((void*)result.Data, checked((int)result.Size)));
                    remaining = LocalFree(result.Data);
                }

                if (remaining != 0)
                {
                    if (!protect) CryptographicOperations.ZeroMemory(output);
                    throw new Win32Exception(Marshal.GetLastPInvokeError(),
                        "Windows could not release the credential buffer.");
                }

                return output;
            }
        }
    }
}

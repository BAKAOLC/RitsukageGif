using System.Security.Cryptography;
using System.Text;

namespace RitsukageGif.Desktop
{
    internal static partial class CredentialVault
    {
        private const int MaximumSecretBytes = 1024 * 1024;
        private const string Service = "RitsukageGif.Credentials.v1";
        private static readonly UTF8Encoding SecretEncoding = new(false, true);

        private static string Profile => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            OperatingSystem.IsWindows()
                ? Path.GetFullPath(ApplicationStorage.DataDirectory).ToUpperInvariant()
                : Path.GetFullPath(ApplicationStorage.DataDirectory))));

        internal static ValueTask<string?> ReadAsync(string key, CancellationToken cancellationToken = default)
        {
            ValidateKey(key);
            cancellationToken.ThrowIfCancellationRequested();
            if (OperatingSystem.IsWindows()) return new(ReadWindowsAsync(key, cancellationToken));
            if (OperatingSystem.IsMacOS()) return new(Task.Run(() => ReadMac(key), cancellationToken));
            if (OperatingSystem.IsLinux()) return new(ReadLinuxAsync(key, cancellationToken));
            throw new PlatformNotSupportedException("The operating system has no supported credential vault.");
        }

        internal static ValueTask WriteAsync(string key, string secret, CancellationToken cancellationToken = default)
        {
            ValidateKey(key);
            ArgumentNullException.ThrowIfNull(secret);
            cancellationToken.ThrowIfCancellationRequested();
            if (SecretEncoding.GetByteCount(secret) > MaximumSecretBytes)
                throw new ArgumentException("The credential exceeds the vault size limit.", nameof(secret));
            if (OperatingSystem.IsWindows()) return new(WriteWindowsAsync(key, secret, cancellationToken));
            if (OperatingSystem.IsMacOS()) return new(Task.Run(() => WriteMac(key, secret), cancellationToken));
            if (OperatingSystem.IsLinux()) return new(WriteLinuxAsync(key, secret, cancellationToken));
            throw new PlatformNotSupportedException("The operating system has no supported credential vault.");
        }

        internal static ValueTask DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            ValidateKey(key);
            cancellationToken.ThrowIfCancellationRequested();
            if (OperatingSystem.IsWindows()) return new(Task.Run(() => DeleteWindows(key), cancellationToken));
            if (OperatingSystem.IsMacOS()) return new(Task.Run(() => DeleteMac(key), cancellationToken));
            if (OperatingSystem.IsLinux()) return new(DeleteLinuxAsync(key, cancellationToken));
            throw new PlatformNotSupportedException("The operating system has no supported credential vault.");
        }

        private static string KeyHash(string key)
        {
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        }

        private static void ValidateKey(string key)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            if (key.Length > 200 ||
                key.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '-' or '_' or '/' or ':')))
                throw new ArgumentException(
                    "Credential keys must contain only ASCII letters, digits, '.', '-', '_', '/', or ':', with at most 200 characters.",
                    nameof(key));
        }
    }
}

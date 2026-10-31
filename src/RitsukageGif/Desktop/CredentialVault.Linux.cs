using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace RitsukageGif.Desktop
{
    internal static partial class CredentialVault
    {
        private static readonly string[] SecretToolLocations = ["/usr/bin/secret-tool", "/usr/local/bin/secret-tool"];

        private static async Task<string?> ReadLinuxAsync(string key, CancellationToken cancellationToken)
        {
            var result = await RunSecretToolAsync("lookup", key, null, cancellationToken).ConfigureAwait(false);
            if (result is { ExitCode: 1, HasError: false }) return null;
            RequireSecretToolSuccess(result.ExitCode, result.HasError);
            return result.Output;
        }

        private static async Task WriteLinuxAsync(string key, string secret, CancellationToken cancellationToken)
        {
            // secret-tool's stdin reader has an 8192-byte buffer and does not safely grow it.
            if (SecretEncoding.GetByteCount(secret) >= 8192)
                throw new ArgumentException(
                    "The Linux credential provider accepts fewer than 8192 UTF-8 bytes per credential.",
                    nameof(secret));
            var result = await RunSecretToolAsync("store", key, secret, cancellationToken).ConfigureAwait(false);
            RequireSecretToolSuccess(result.ExitCode, result.HasError);
        }

        private static async Task DeleteLinuxAsync(string key, CancellationToken cancellationToken)
        {
            var result = await RunSecretToolAsync("clear", key, null, cancellationToken).ConfigureAwait(false);
            if (result is { ExitCode: 1, HasError: false }) return;
            RequireSecretToolSuccess(result.ExitCode, result.HasError);
        }

        private static void RequireSecretToolSuccess(int exitCode, bool hasError)
        {
            if (exitCode != 0 || hasError)
                throw new IOException(
                    "The Linux credential vault is unavailable or locked. An unlocked Secret Service is required.");
        }

        private static async Task<(int ExitCode, string Output, bool HasError)> RunSecretToolAsync(
            string operation, string key, string? secret, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var executable = SecretToolLocations.FirstOrDefault(File.Exists)
                             ?? throw new PlatformNotSupportedException(
                                 "Linux credential storage requires the system secret-tool utility and Secret Service.");
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = SecretEncoding,
                StandardErrorEncoding = Encoding.UTF8,
            };
            start.ArgumentList.Add(operation);
            if (operation == "store") start.ArgumentList.Add("--label=RitsukageGif credential");
            start.ArgumentList.Add("application");
            start.ArgumentList.Add(Service);
            start.ArgumentList.Add("profile");
            start.ArgumentList.Add(Profile);
            start.ArgumentList.Add("key");
            start.ArgumentList.Add(KeyHash(key));
            using var process = new Process();
            process.StartInfo = start;
            try
            {
                if (!process.Start()) throw new IOException("The Linux credential provider could not start.");
            }
            catch (Win32Exception)
            {
                throw new IOException("The Linux credential provider could not start.");
            }

            var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var error = process.StandardError.ReadToEndAsync(CancellationToken.None);
            try
            {
                if (secret is not null)
                    await process.StandardInput.WriteAsync(secret.AsMemory(), cancellationToken).ConfigureAwait(false);
                process.StandardInput.Close();
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                return (process.ExitCode, await output.ConfigureAwait(false),
                    !string.IsNullOrEmpty(await error.ConfigureAwait(false)));
            }
            catch
            {
                try
                {
                    if (!process.HasExited) process.Kill(true);
                }
                catch (InvalidOperationException)
                {
                }

                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                await Task.WhenAll(output, error).ConfigureAwait(false);
                throw;
            }
        }
    }
}

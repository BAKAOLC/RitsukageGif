using System.Diagnostics;

namespace RitsukageGif.Desktop
{
    internal sealed class SystemDocumentLauncher(Func<ProcessStartInfo, Process?>? startProcess = null)
        : IDocumentLauncher
    {
        public async Task OpenAsync(string path, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            path = Path.GetFullPath(path);
            if (OperatingSystem.IsWindows())
            {
                (startProcess ?? Process.Start)(new(path) { UseShellExecute = true, Verb = "open" })?.Dispose();
                return;
            }

            if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
                throw new PlatformNotSupportedException(
                    "Opening documents in a default application is not supported on this platform.");
            var start = new ProcessStartInfo(OperatingSystem.IsMacOS() ? "/usr/bin/open" : "xdg-open")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true,
                RedirectStandardOutput = true,
            };
            start.ArgumentList.Add(path);
            using var process = (startProcess ?? Process.Start)(start) ??
                                throw new InvalidOperationException("The default document launcher did not start.");
            var error = process.StandardError.ReadToEndAsync(CancellationToken.None);
            var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            try
            {
                await process.WaitForExitAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                try
                {
                    if (!process.HasExited) process.Kill();
                }
                catch (InvalidOperationException)
                {
                }

                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                await Task.WhenAll(error, output).ConfigureAwait(false);
                throw;
            }

            await Task.WhenAll(error, output).ConfigureAwait(false);
            if (process.ExitCode != 0)
                throw new InvalidOperationException("No application accepted the generated document.");
        }
    }
}

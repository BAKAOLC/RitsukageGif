using System.ComponentModel;
using System.Diagnostics;

namespace RitsukageGif.Desktop
{
    internal static class LinuxFileReveal
    {
        private static readonly ILogger Logger = LogHub.GetLogger("Desktop.Shell");

        internal static async Task RevealAsync(string path)
        {
            try
            {
                using var process = Process.Start(CreateRequest(path)) ??
                                    throw new IOException("The file manager request could not be started.");
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (timeout.IsCancellationRequested)
                {
                    try
                    {
                        process.Kill();
                    }
                    catch (InvalidOperationException) when (process.HasExited)
                    {
                    }

                    await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                    await Task.WhenAll(output, error).ConfigureAwait(false);
                    throw new TimeoutException("The file manager did not answer the reveal request.");
                }

                await output.ConfigureAwait(false);
                var details = await error.ConfigureAwait(false);
                if (process.ExitCode == 0) return;
                Logger.ZLogDebug($"FileManager1 reveal unavailable. ExitCode={process.ExitCode} Error={details}");
            }
            catch (Exception exception) when (exception is Win32Exception or IOException or TimeoutException)
            {
                Logger.ZLogDebug($"FileManager1 reveal unavailable. Error={exception.Message}");
            }

            var start = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
            start.ArgumentList.Add(Path.GetDirectoryName(path)!);
            using var fallback = Process.Start(start) ?? throw new IOException("The file manager could not be opened.");
        }

        private static ProcessStartInfo CreateRequest(string path)
        {
            var uri = new Uri(Path.GetFullPath(path)).AbsoluteUri.Replace(@"\", @"\\").Replace("'", @"\'");
            var start = new ProcessStartInfo("gdbus")
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            foreach (var argument in new[]
                     {
                         "call", "--session", "--timeout", "3", "--dest", "org.freedesktop.FileManager1",
                         "--object-path",
                         "/org/freedesktop/FileManager1", "--method", "org.freedesktop.FileManager1.ShowItems",
                         "['" + uri + "']", "''",
                     }) start.ArgumentList.Add(argument);
            return start;
        }
    }
}

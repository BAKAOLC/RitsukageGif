using System.Diagnostics;
using System.Text.Json;

namespace RitsukageGif.Desktop
{
    internal static partial class MacWindowSystem
    {
        private static async Task<JsonDocument> ReadCommandAsync(string command, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "RitsukageGif.Capture"))
                { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            start.ArgumentList.Add(command);
            using var process = Process.Start(start) ?? throw new IOException(UiText.Get("CaptureHelperMissing"));
            var output = process.StandardOutput.ReadToEndAsync(token);
            process.BeginErrorReadLine();
            try
            {
                await process.WaitForExitAsync(token).WaitAsync(TimeSpan.FromMinutes(1), token).ConfigureAwait(false);
                if (process.ExitCode != 0)
                    throw new IOException(UiText.Get(process.ExitCode == 77
                        ? "PlatformCapturePermission"
                        : "CaptureFailed"));
                return JsonDocument.Parse(await output.ConfigureAwait(false));
            }
            finally
            {
                if (!process.HasExited)
                {
                    try
                    {
                        process.Kill();
                    }
                    catch (InvalidOperationException) when (process.HasExited)
                    {
                    }

                    await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                }

                try
                {
                    await output.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                }
            }
        }
    }
}

using System.Diagnostics;
using System.Text.Json;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PortableForegroundPolicy
    {
        private async Task RunHelperAsync(CancellationToken token)
        {
            var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "RitsukageGif.Foreground"))
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var process = Process.Start(start) ??
                                throw new IOException("Foreground policy helper could not start.");
            var errors = process.StandardError.ReadToEndAsync(token);
            try
            {
                while (await process.StandardOutput.ReadLineAsync(token).ConfigureAwait(false) is { } line)
                {
                    using var json = JsonDocument.Parse(line);
                    var state = json.RootElement;
                    Publish(new(state.GetProperty("program").GetString(), state.GetProperty("fullscreen").GetBoolean(),
                        state.GetProperty("programAvailable").GetBoolean(),
                        state.GetProperty("fullscreenAvailable").GetBoolean(),
                        state.TryGetProperty("reason", out var reason) && reason.GetString() == "permission"
                            ? "HotkeyPolicyPermissionRequired"
                            : "HotkeyPolicyObserverUnavailable"));
                }

                token.ThrowIfCancellationRequested();
                await process.WaitForExitAsync(token).ConfigureAwait(false);
                throw new IOException(
                    $"Foreground policy helper stopped. ExitCode={process.ExitCode}; {await errors.ConfigureAwait(false)}");
            }
            finally
            {
                try
                {
                    if (!process.HasExited)
                    {
                        await process.StandardInput.WriteLineAsync("stop").ConfigureAwait(false);
                        await process.StandardInput.FlushAsync(CancellationToken.None).ConfigureAwait(false);
                        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                        try
                        {
                            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            process.Kill();
                            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                        }
                    }
                }
                catch (InvalidOperationException)
                {
                }
                catch (IOException)
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                }

                try
                {
                    await errors.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                }
            }
        }
    }
}

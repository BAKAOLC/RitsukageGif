using System.Diagnostics;
using System.Text.Json;

namespace RitsukageGif.Desktop
{
    internal static class NativePrintCapabilities
    {
        private static readonly Lock Gate = new();
        private static readonly ILogger Logger = LogHub.GetLogger("Desktop.PrintCapabilities");
        private static Task<PrintCapabilities>? _query;
        private static PrintCapabilities _current = new(OperatingSystem.IsWindows());

        internal static PrintCapabilities Current => Volatile.Read(ref _current);

        internal static Task<PrintCapabilities> GetAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Task<PrintCapabilities> query;
            lock (Gate)
            {
                if (_query is null || _query.IsCompleted)
                    _query = QueryAndRememberAsync();
                query = _query;
            }

            return query.WaitAsync(token);
        }

        private static async Task<PrintCapabilities> QueryAndRememberAsync()
        {
            var result = await QueryAsync(null, CancellationToken.None).ConfigureAwait(false);
            Volatile.Write(ref _current, result);
            return result;
        }

        private static async Task<PrintCapabilities> QueryAsync(Func<ProcessStartInfo>? startInfoFactory,
            CancellationToken token)
        {
            switch (startInfoFactory)
            {
                case null when OperatingSystem.IsWindows():
                    return new(true);
                case null when !OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS():
                    return new(false, "The operating system has no supported native print backend.");
            }

            var helper = Path.Combine(AppContext.BaseDirectory, "RitsukageGif.Print");
            if (startInfoFactory is null && !File.Exists(helper))
                return new(false, "The native print helper is not installed.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            Process? process = null;
            try
            {
                var start = startInfoFactory?.Invoke() ?? new(helper);
                start.UseShellExecute = false;
                start.CreateNoWindow = true;
                start.WindowStyle = ProcessWindowStyle.Hidden;
                start.RedirectStandardInput = start.RedirectStandardOutput = start.RedirectStandardError = true;
                start.ArgumentList.Add("capabilities");
                process = Process.Start(start) ?? throw new IOException("The print capability helper did not start.");
                process.StandardInput.Close();
                var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
                var error = process.StandardError.ReadToEndAsync(CancellationToken.None);
                try
                {
                    await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                }
                catch
                {
                    await PrintWorkerTransport.StopAsync(process).ConfigureAwait(false);
                    await Task.WhenAll(output, error).ConfigureAwait(false);
                    throw;
                }

                var json = await output.ConfigureAwait(false);
                var detail = await error.ConfigureAwait(false);
                if (process.ExitCode != 0)
                {
                    Logger.ZLogWarning(
                        $"Native print capability helper failed. ExitCode={process.ExitCode} Detail={detail}");
                    return new(false, "The native print service could not be queried.");
                }

                return JsonSerializer.Deserialize(json, NativePrintJsonContext.Default.PrintCapabilities) ??
                       new(false, "The native print service returned no capabilities.");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error)
            {
                Logger.ZLogWarning(error, $"Native print capabilities are unavailable.");
                return new(false, "The native print service is unavailable.");
            }
            finally
            {
                process?.Dispose();
            }
        }
    }
}

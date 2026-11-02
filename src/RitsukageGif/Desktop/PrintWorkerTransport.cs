using System.Diagnostics;
using System.Globalization;

namespace RitsukageGif.Desktop
{
    internal static class PrintWorkerTransport
    {
        private static readonly SemaphoreSlim Gate = new(1);
        private static readonly ILogger Logger = LogHub.GetLogger("Desktop.PrintWorker");

        internal static Task<bool> RunAsync(nint owner, string title, string workerSwitch,
            Func<string, CancellationToken, Task<string>> writeInput, Func<ProcessStartInfo> startInfoFactory,
            Func<string, string, CancellationToken, Task<string>>? prepare, CancellationToken token)
        {
            return RunCoreAsync(owner.ToString(CultureInfo.InvariantCulture), title, workerSwitch, writeInput,
                startInfoFactory, prepare, token);
        }

        internal static Task<bool> RunNativeAsync(string parentWindow, string title, string workerSwitch,
            Func<string, CancellationToken, Task<string>> writeInput, Func<ProcessStartInfo> startInfoFactory,
            Func<string, string, CancellationToken, Task<string>>? prepare, CancellationToken token)
        {
            return RunCoreAsync(parentWindow, title, workerSwitch, writeInput, startInfoFactory, prepare, token);
        }

        private static async Task<bool> RunCoreAsync(string owner, string title, string workerSwitch,
            Func<string, CancellationToken, Task<string>> writeInput, Func<ProcessStartInfo> startInfoFactory,
            Func<string, string, CancellationToken, Task<string>>? prepare, CancellationToken token)
        {
            await Gate.WaitAsync(token).ConfigureAwait(false);
            OwnedWorkerDirectory? temporary = null;
            Process? process = null;
            try
            {
                temporary = OwnedWorkerDirectory.Create("print");
                var input = await writeInput(temporary.DirectoryPath, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                var start = startInfoFactory();
                start.UseShellExecute = false;
                start.CreateNoWindow = true;
                start.WindowStyle = ProcessWindowStyle.Normal;
                start.RedirectStandardInput = start.RedirectStandardOutput = start.RedirectStandardError = true;
                start.ArgumentList.Add(workerSwitch);
                start.ArgumentList.Add(input);
                start.ArgumentList.Add(owner);
                start.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
                start.ArgumentList.Add(title);
                process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start the print worker.");
                Task<string>? output = null;
                var firstLine = string.Empty;
                var error = process.StandardError.ReadToEndAsync(CancellationToken.None);
                Logger.ZLogDebug($"Isolated printing started. WorkerProcessId={process.Id}");
                try
                {
                    if (prepare is not null)
                    {
                        firstLine = await process.StandardOutput.ReadLineAsync(token).ConfigureAwait(false)
                                    ?? throw new InvalidDataException(
                                        "The print worker closed before selecting paper.");
                        if (firstLine != "canceled")
                        {
                            var reply = await prepare(temporary.DirectoryPath, firstLine, token).ConfigureAwait(false);
                            await process.StandardInput.WriteLineAsync(reply.AsMemory(), token).ConfigureAwait(false);
                            await process.StandardInput.FlushAsync(token).ConfigureAwait(false);
                            firstLine = string.Empty;
                        }
                    }

                    output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
                    await process.WaitForExitAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    await StopAsync(process).ConfigureAwait(false);
                    output ??= process.StandardOutput.ReadToEndAsync(CancellationToken.None);
                    await Task.WhenAll(output, error).ConfigureAwait(false);
                    Logger.ZLogInformation($"Printing canceled; the isolated worker exited.");
                    throw;
                }
                catch
                {
                    await StopAsync(process).ConfigureAwait(false);
                    output ??= process.StandardOutput.ReadToEndAsync(CancellationToken.None);
                    await Task.WhenAll(output, error).ConfigureAwait(false);
                    throw;
                }

                var message = firstLine + await output.ConfigureAwait(false);
                var detail = await error.ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (process.ExitCode != 0)
                {
                    Logger.ZLogWarning($"Print worker failed. ExitCode={process.ExitCode} Detail={detail}");
                    throw new InvalidOperationException("The print worker failed. See the application logs.");
                }

                return message.Trim() switch
                {
                    "printed" => true, "canceled" => false,
                    _ => throw new InvalidDataException("The print worker returned an invalid response."),
                };
            }
            finally
            {
                try
                {
                    if (process is not null)
                        try
                        {
                            if (!process.HasExited) await StopAsync(process).ConfigureAwait(false);
                        }
                        finally
                        {
                            process.Dispose();
                        }

                    temporary?.Dispose();
                }
                finally
                {
                    Gate.Release();
                }
            }
        }

        internal static async Task StopAsync(Process process)
        {
            if (process.HasExited) return;
            try
            {
                using var grace = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                await process.StandardInput.WriteLineAsync("cancel".AsMemory(), grace.Token).ConfigureAwait(false);
                await process.StandardInput.FlushAsync(grace.Token).ConfigureAwait(false);
                await process.WaitForExitAsync(grace.Token).ConfigureAwait(false);
            }
            catch (Exception error) when (error is OperationCanceledException or IOException
                                              or InvalidOperationException)
            {
                Logger.ZLogDebug(
                    $"Print worker did not complete its cooperative stop. ExceptionType={error.GetType().Name}");
            }

            if (!process.HasExited)
            {
                try
                {
                    process.Kill(true);
                }
                catch (InvalidOperationException)
                {
                }

                await process.WaitForExitAsync().ConfigureAwait(false);
                Logger.ZLogDebug($"Unresponsive print worker terminated.");
            }
        }

        internal static int RunWorker(int parentProcessId, Func<CancellationToken, bool> print,
            Action initialize, Action shutdown, Action<string>? command = null)
        {
            initialize();
            using var lifetime = new CancellationTokenSource();
            try
            {
                using var parent = Process.GetProcessById(parentProcessId);
                _ = parent.WaitForExitAsync(CancellationToken.None)
                    .ContinueWith(_ => Environment.Exit(0), TaskScheduler.Default);
                // ReSharper disable once AccessToDisposedClosure
                _ = Task.Run(() => ReadCancellationAsync(lifetime, command), CancellationToken.None);
                var printed = print(lifetime.Token);
                Console.WriteLine(printed ? "printed" : "canceled");
                return 0;
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
            {
                Console.WriteLine("canceled");
                return 0;
            }
            catch (Exception error)
            {
                Logger.ZLogError(error, $"Print worker failed.");
                Console.Error.WriteLine(error.Message);
                return 1;
            }
            finally
            {
                shutdown();
            }
        }

        private static async Task ReadCancellationAsync(CancellationTokenSource lifetime, Action<string>? command)
        {
            try
            {
                while (await Console.In.ReadLineAsync(CancellationToken.None).ConfigureAwait(false) is { } line)
                    if (line == "cancel")
                    {
                        await lifetime.CancelAsync().ConfigureAwait(false);
                        return;
                    }
                    else
                    {
                        command?.Invoke(line);
                    }

                await lifetime.CancelAsync().ConfigureAwait(false);
            }
            catch (Exception error) when (error is IOException or ObjectDisposedException)
            {
                Logger.ZLogDebug($"Print cancellation channel closed. ExceptionType={error.GetType().Name}");
            }
        }
    }
}

using System.Diagnostics;
using System.Text.Json;

namespace RitsukageGif.Desktop
{
    internal static class AnimationImportProcess
    {
        private static readonly ILogger Logger = LogHub.GetLogger("Desktop.AnimationImport");

        internal static async Task<AnimationEditDocument> OpenAsync(string path,
            Func<ProcessStartInfo>? startInfoFactory = null, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            var directory = Path.Combine(Path.GetTempPath(), "RitsukageGif-edit-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var adopted = false;
            try
            {
                var start = (startInfoFactory ?? Program.CreateSelfStartInfo)();
                start.UseShellExecute = false;
                start.CreateNoWindow = true;
                start.WindowStyle = ProcessWindowStyle.Hidden;
                start.RedirectStandardOutput = true;
                start.RedirectStandardError = true;
                start.ArgumentList.Add("--animation-import-worker");
                start.ArgumentList.Add(Path.GetFullPath(path));
                start.ArgumentList.Add(directory);
                using var process = Process.Start(start) ?? throw new InvalidOperationException();
                var outputTask = process.StandardOutput.ReadToEndAsync(token);
                var errorTask = process.StandardError.ReadToEndAsync(token);
                Logger.ZLogDebug($"Isolated animation import started. WorkerProcessId={process.Id}");
                try
                {
                    await process.WaitForExitAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    try
                    {
                        process.Kill(true);
                    }
                    catch (InvalidOperationException)
                    {
                    }

                    await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                    await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
                    throw;
                }

                var output = await outputTask.ConfigureAwait(false);
                var error = await errorTask.ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (process.ExitCode != 0)
                {
                    Logger.ZLogWarning($"Isolated animation import failed. ExitCode={process.ExitCode} Detail={error}");
                    throw new InvalidOperationException(UiText.Format("AnimationImportWorkerFailed", process.ExitCode,
                        DiagnosticsActions.LogDirectory));
                }

                AnimationEditTransfer transfer;
                try
                {
                    transfer = JsonSerializer.Deserialize(output,
                                   AnimationImportJsonContext.Default.AnimationEditTransfer)
                               ?? throw new InvalidDataException();
                }
                catch (Exception failure) when (failure is JsonException or InvalidDataException)
                {
                    throw new InvalidOperationException(UiText.Format("AnimationImportWorkerFailed", process.ExitCode,
                        DiagnosticsActions.LogDirectory), failure);
                }

                var document = AnimationEditDocument.AdoptTransfer(directory, transfer);
                adopted = true;
                Logger.ZLogInformation($"Isolated animation import completed. Frames={document.Frames.Count}");
                return document;
            }
            finally
            {
                if (!adopted)
                    try
                    {
                        Directory.Delete(directory, true);
                    }
                    catch (DirectoryNotFoundException)
                    {
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                    {
                        Logger.ZLogWarning(error, $"Animation import temporary storage cleanup deferred.");
                    }
            }
        }

        internal static int RunWorker(string input, string ownedDirectory)
        {
            try
            {
                DiagnosticLog.Initialize("animation-import");
                Logger.ZLogInformation($"Animation import worker decoding started.");
                var transfer = AnimationEditDocument.PrepareImport(input, ownedDirectory);
                using var output = Console.OpenStandardOutput();
                JsonSerializer.Serialize(output, transfer,
                    AnimationImportJsonContext.Default.AnimationEditTransfer);
                Logger.ZLogInformation($"Animation import worker decoding completed. Frames={transfer.Frames.Length}");
                return 0;
            }
            catch (Exception error)
            {
                Logger.ZLogError(error, $"Animation import worker decoding failed.");
                Console.Error.WriteLine(error.Message);
                return 1;
            }
            finally
            {
                DiagnosticLog.Shutdown();
            }
        }
    }
}

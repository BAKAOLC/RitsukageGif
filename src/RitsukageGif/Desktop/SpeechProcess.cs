using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace RitsukageGif.Desktop
{
    internal static class SpeechProcess
    {
        private static readonly ILogger Logger = LogHub.GetLogger("Desktop.SpeechWorker");

        internal static async Task<string> RequestAsync(string mode, SpeechRequest? request, CancellationToken token,
            Func<ProcessStartInfo>? factory = null)
        {
            token.ThrowIfCancellationRequested();
            using var temporary = OwnedWorkerDirectory.Create("speech");
            var directory = temporary.DirectoryPath;
            Process? process = null;
            try
            {
                if (request is not null)
                    await File.WriteAllTextAsync(Path.Combine(directory, "request.json"),
                            JsonSerializer.Serialize(request, SpeechJsonContext.Default.SpeechRequest), token)
                        .ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                var start = (factory ?? Program.CreateSelfStartInfo)();
                start.UseShellExecute = false;
                start.CreateNoWindow = true;
                start.WindowStyle = ProcessWindowStyle.Hidden;
                start.RedirectStandardInput = start.RedirectStandardOutput = start.RedirectStandardError = true;
                start.ArgumentList.Add("--speech-worker");
                start.ArgumentList.Add(mode);
                start.ArgumentList.Add(directory);
                start.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
                process = Process.Start(start) ??
                          throw new InvalidOperationException("Cannot start the speech worker.");
                var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
                var error = process.StandardError.ReadToEndAsync(CancellationToken.None);
                Logger.ZLogDebug($"Speech worker started. Mode={mode} WorkerProcessId={process.Id}");
                try
                {
                    await process.WaitForExitAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)when (token.IsCancellationRequested)
                {
                    await StopAsync(process).ConfigureAwait(false);
                    await Task.WhenAll(output, error).ConfigureAwait(false);
                    throw;
                }

                var result = await output.ConfigureAwait(false);
                var detail = await error.ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (process.ExitCode != 0)
                {
                    Logger.ZLogWarning(
                        $"Speech worker failed. Mode={mode} ExitCode={process.ExitCode} Detail={detail}");
                    throw new InvalidOperationException("System speech failed. See the application logs.");
                }

                return result;
            }
            finally
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
            }
        }

        private static async Task StopAsync(Process process)
        {
            if (process.HasExited) return;
            try
            {
                using var grace = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                await process.StandardInput.WriteLineAsync("cancel".AsMemory(), grace.Token).ConfigureAwait(false);
                await process.StandardInput.FlushAsync(grace.Token).ConfigureAwait(false);
                await process.WaitForExitAsync(grace.Token).ConfigureAwait(false);
            }
            catch (Exception error)when
                (error is OperationCanceledException or IOException or InvalidOperationException)
            {
                Logger.ZLogDebug($"Speech cooperative stop did not complete. ExceptionType={error.GetType().Name}");
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

                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                Logger.ZLogDebug($"Unresponsive speech worker terminated.");
            }
        }

        internal static int RunWorker(string mode, string directory, int parentProcessId)
        {
            DiagnosticLog.Initialize("speech-worker");
            using var lifetime = new CancellationTokenSource();
            try
            {
                using var parent = Process.GetProcessById(parentProcessId);
                _ = parent.WaitForExitAsync(CancellationToken.None)
                    .ContinueWith(_ => Environment.Exit(0), TaskScheduler.Default);
                // ReSharper disable once AccessToDisposedClosure
                _ = Task.Run(() => ReadCancellationAsync(lifetime), CancellationToken.None);
                RunWorkerAsync(mode, directory, lifetime.Token).GetAwaiter().GetResult();
                return 0;
            }
            catch (OperationCanceledException)when (lifetime.IsCancellationRequested)
            {
                Logger.ZLogDebug($"Speech worker canceled.");
                return 0;
            }
            catch (Exception error)
            {
                Logger.ZLogError(error, $"Speech worker failed.");
                Console.Error.WriteLine(error.Message);
                return 1;
            }
            finally
            {
                DiagnosticLog.Shutdown();
            }
        }

        private static async Task RunWorkerAsync(string mode, string directory, CancellationToken token)
        {
            var capabilities = await SystemSpeechBackend.GetCapabilitiesAsync(token).ConfigureAwait(false);
            Logger.ZLogDebug(
                $"System speech capabilities loaded. Available={capabilities.Available} VoiceCount={capabilities.Voices.Length}");
            if (mode == "capabilities")
            {
                Console.WriteLine(JsonSerializer.Serialize(capabilities, SpeechJsonContext.Default.SpeechCapabilities));
                return;
            }

            var json = await File.ReadAllTextAsync(Path.Combine(directory, "request.json"), token)
                .ConfigureAwait(false);
            var request = JsonSerializer.Deserialize(json, SpeechJsonContext.Default.SpeechRequest) ??
                          throw new InvalidDataException("The speech request is empty.");
            if (request.Text.Length > 200000)
                throw new InvalidDataException("The speech request exceeds the maximum text length.");
            var voice = SpeechVoiceSelection.Select(capabilities, request.Language) ??
                        throw new NotSupportedException("No system voice is installed for the requested language.");
            var playback = new SoundPlayback();
            var index = 0;
            await using var registration = token.Register(() => { _ = playback.StopAsync(); });
            try
            {
                foreach (var segment in SpeechTextSegments.Split(request.Text))
                {
                    token.ThrowIfCancellationRequested();
                    var output = Path.Combine(directory, $"segment-{index++:D4}.wav");
                    try
                    {
                        await SystemSpeechBackend.SynthesizeAsync(segment, voice, output, token).ConfigureAwait(false);
                        Logger.ZLogDebug($"Speech segment synthesized. SegmentIndex={index} Language={voice.Language}");
                        token.ThrowIfCancellationRequested();
                        await using (var wave = File.OpenRead(output))
                        {
                            var header = new byte[12];
                            await wave.ReadExactlyAsync(header, token).ConfigureAwait(false);
                            if (!header.AsSpan(0, 4).SequenceEqual("RIFF"u8) ||
                                !header.AsSpan(8, 4).SequenceEqual("WAVE"u8) ||
                                wave.Length <= 44)
                                throw new InvalidDataException("The system speech engine returned invalid audio.");
                        }

                        var speaking = playback.PlayAsync(output);
                        if (token.IsCancellationRequested) await playback.StopAsync().ConfigureAwait(false);
                        await speaking.ConfigureAwait(false);
                        token.ThrowIfCancellationRequested();
                        Logger.ZLogDebug($"Speech segment playback completed. SegmentIndex={index}");
                    }
                    finally
                    {
                        File.Delete(output);
                    }
                }

                await Console.Out.WriteLineAsync("completed").ConfigureAwait(false);
            }
            finally
            {
                await playback.StopAsync().ConfigureAwait(false);
            }
        }

        private static async Task ReadCancellationAsync(CancellationTokenSource lifetime)
        {
            try
            {
                while (await Console.In.ReadLineAsync(CancellationToken.None).ConfigureAwait(false) is { } line)
                    if (line == "cancel")
                    {
                        await lifetime.CancelAsync().ConfigureAwait(false);
                        return;
                    }

                await lifetime.CancelAsync().ConfigureAwait(false);
            }
            catch (Exception error)when (error is IOException or ObjectDisposedException)
            {
                Logger.ZLogDebug($"Speech cancellation channel closed. ExceptionType={error.GetType().Name}");
            }
        }
    }
}

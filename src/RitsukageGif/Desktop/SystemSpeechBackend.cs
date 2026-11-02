using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace RitsukageGif.Desktop
{
    internal static partial class SystemSpeechBackend
    {
        internal static async Task<SpeechCapabilities> GetCapabilitiesAsync(CancellationToken token)
        {
            if (OperatingSystem.IsWindows())
                return await Task.Run(WindowsSpeechSynthesis.GetCapabilities, token).ConfigureAwait(false);
            var engine = FindEngine();
            if (engine is null)
                return new(false, [],
                    Reason: OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()
                        ? SpeechUnavailableReason.EngineMissing
                        : SpeechUnavailableReason.UnsupportedPlatform,
                    Detail: OperatingSystem.IsLinux() ? "espeak-ng" : "say");
            var start = Start(engine);
            if (OperatingSystem.IsMacOS())
            {
                start.ArgumentList.Add("-v");
                start.ArgumentList.Add("?");
            }
            else
            {
                start.ArgumentList.Add("--voices");
            }

            var output = await RunAsync(start, token).ConfigureAwait(false);
            var voices = new List<SpeechVoice>();
            foreach (var line in output.Split('\n'))
                if (OperatingSystem.IsMacOS())
                {
                    var match = MacVoiceLine().Match(line);
                    if (match.Success)
                        voices.Add(new(match.Groups[1].Value.Trim(), match.Groups[1].Value.Trim(),
                            match.Groups[2].Value.Replace('_', '-')));
                }
                else
                {
                    var values = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    if (values.Length >= 5 && int.TryParse(values[0], out _))
                        voices.Add(new(values[1], values[3].Replace('_', ' '), values[1],
                            [.. AlternateLanguage().Matches(line).Select(match => match.Groups[1].Value)]));
                }

            if (OperatingSystem.IsMacOS())
            {
                var format = Start(engine);
                format.ArgumentList.Add("--file-format=?");
                var formats = await RunAsync(format, token).ConfigureAwait(false);
                if (!formats.Contains("WAVE", StringComparison.OrdinalIgnoreCase))
                    return new(false, [], Reason: SpeechUnavailableReason.EngineError,
                        Detail: "System speech does not support WAVE output.");
            }

            var preferred = voices.FirstOrDefault(voice =>
                                voice.Language.Equals(CultureInfo.CurrentCulture.Name,
                                    StringComparison.OrdinalIgnoreCase)) ??
                            voices.FirstOrDefault();
            return new(voices.Count > 0, [.. voices], preferred?.Id,
                voices.Count == 0 ? SpeechUnavailableReason.NoVoices : SpeechUnavailableReason.None);
        }

        internal static async Task SynthesizeAsync(string text, SpeechVoice voice, string path, CancellationToken token)
        {
            if (OperatingSystem.IsWindows())
            {
                await Task.Run(() => WindowsSpeechSynthesis.Synthesize(text, voice.Id, path, token), token)
                    .ConfigureAwait(false);
                return;
            }

            var engine = FindEngine() ??
                         throw new InvalidOperationException("The system speech engine is unavailable.");
            var input = path + ".txt";
            try
            {
                await File.WriteAllTextAsync(input, text, token).ConfigureAwait(false);
                var start = Start(engine);
                if (OperatingSystem.IsMacOS())
                {
                    start.ArgumentList.Add("--file-format=WAVE");
                    start.ArgumentList.Add("--data-format=LEI16@22050");
                    start.ArgumentList.Add("-o");
                    start.ArgumentList.Add(path);
                    start.ArgumentList.Add("-f");
                    start.ArgumentList.Add(input);
                    start.ArgumentList.Add("-v");
                    start.ArgumentList.Add(voice.Id);
                }
                else
                {
                    start.ArgumentList.Add("-w");
                    start.ArgumentList.Add(path);
                    start.ArgumentList.Add("-f");
                    start.ArgumentList.Add(input);
                    start.ArgumentList.Add("-v");
                    start.ArgumentList.Add(voice.Id);
                    start.ArgumentList.Add("-b");
                    start.ArgumentList.Add("1");
                }

                _ = await RunAsync(start, token).ConfigureAwait(false);
            }
            finally
            {
                File.Delete(input);
            }
        }

        private static string? FindEngine()
        {
            if (OperatingSystem.IsMacOS()) return File.Exists("/usr/bin/say") ? "/usr/bin/say" : null;
            if (!OperatingSystem.IsLinux()) return null;
            return (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
                .Select(directory => Path.Combine(directory, "espeak-ng")).FirstOrDefault(File.Exists);
        }

        private static ProcessStartInfo Start(string path)
        {
            return new(path)
            {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
        }

        private static async Task<string> RunAsync(ProcessStartInfo start, CancellationToken token)
        {
            using var process = Process.Start(start) ??
                                throw new InvalidOperationException("Cannot start the system speech engine.");
            var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var error = process.StandardError.ReadToEndAsync(CancellationToken.None);
            try
            {
                await process.WaitForExitAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)when (token.IsCancellationRequested)
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

            var text = await output.ConfigureAwait(false);
            var detail = await error.ConfigureAwait(false);
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"System speech exited with code {process.ExitCode}: {detail}");
            return text + "\n" + detail;
        }

        [GeneratedRegex(@"\(([A-Za-z][A-Za-z0-9-]*)\s+\d+\)", RegexOptions.CultureInvariant)]
        private static partial Regex AlternateLanguage();

        [GeneratedRegex(@"^(.*?)\s+([A-Za-z]{2,3}[_-][A-Za-z0-9_-]+)\s+#", RegexOptions.CultureInvariant)]
        private static partial Regex MacVoiceLine();
    }
}

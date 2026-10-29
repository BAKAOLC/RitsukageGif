using System.Diagnostics;

namespace RitsukageGif.Core
{
    public sealed record VideoRecordingCapabilities(
        string? Executable,
        bool Mp4,
        bool WebM,
        IReadOnlySet<string> AudioInputFormats,
        bool Mp4Audio = false,
        bool WebMAudio = false,
        bool Mp4Hdr = false,
        bool WebMHdr = false)
    {
        public static async Task<VideoRecordingCapabilities> ProbeAsync(CancellationToken token = default)
        {
            var executable = FfmpegEncoder.FindExecutable();
            if (executable is null) return new(null, false, false, new HashSet<string>());
            var encoders = await QueryAsync(executable, "-encoders", token);
            var devices = await QueryAsync(executable, "-devices", token);
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in devices.Split('\n'))
            {
                var columns = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (columns.Length > 1 && columns[0] is "D" or "DE") names.Add(columns[1]);
            }

            var encoderNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in encoders.Split('\n'))
            {
                var columns = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (columns.Length > 1) encoderNames.Add(columns[1]);
            }

            var hevc = encoderNames.Contains("libx265") &&
                       (await QueryAsync(executable, "-h", token, "encoder=libx265")).Contains("yuv420p10le",
                           StringComparison.Ordinal);
            var vp9 = encoderNames.Contains("libvpx-vp9") &&
                      (await QueryAsync(executable, "-h", token, "encoder=libvpx-vp9")).Contains("yuv420p10le",
                          StringComparison.Ordinal);
            return new(executable, encoderNames.Contains("libx264"),
                encoderNames.Contains("libvpx-vp9"), names, encoderNames.Contains("aac"),
                encoderNames.Contains("libopus"), hevc, vp9);
        }

        private static async Task<string> QueryAsync(string executable, string argument, CancellationToken token,
            string? value = null)
        {
            using var process = new Process();
            process.StartInfo = new(executable)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            process.StartInfo.ArgumentList.Add("-hide_banner");
            process.StartInfo.ArgumentList.Add(argument);
            if (value is not null) process.StartInfo.ArgumentList.Add(value);
            token.ThrowIfCancellationRequested();
            process.Start();
            var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var errors = process.StandardError.ReadToEndAsync(CancellationToken.None);
            try
            {
                await process.WaitForExitAsync(token).WaitAsync(TimeSpan.FromSeconds(10), token);
                var result = await output;
                var error = await errors;
                if (process.ExitCode != 0)
                    throw new InvalidOperationException(UiText.Format("FfmpegFailed", process.ExitCode, error));
                return result;
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                    await process.WaitForExitAsync(CancellationToken.None);
                }

                await output;
                await errors;
            }
        }
    }
}

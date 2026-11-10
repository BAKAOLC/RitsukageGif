using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace RitsukageGif.Desktop
{
    internal static partial class RecordingCameraCatalog
    {
        internal static async Task<RecordingCameraCatalogResult> ListAsync(CancellationToken token = default)
        {
            var executable = FfmpegEncoder.FindExecutable();
            if (executable is null) return new([], "FFmpeg is unavailable.");
            if (OperatingSystem.IsLinux()) return ListLinux();
            var format = OperatingSystem.IsWindows() ? "dshow" : OperatingSystem.IsMacOS() ? "avfoundation" : null;
            if (format is null) return new([], "Camera capture is unavailable on this platform.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            using var process = new Process();
            process.StartInfo = RecordingCameraProcess.StartInfo(executable);
            foreach (var argument in new[] { "-hide_banner", "-list_devices", "true", "-f", format, "-i", "dummy" })
                process.StartInfo.ArgumentList.Add(argument);
            var devices = new List<RecordingCameraDevice>();
            try
            {
                process.Start();
                // ReSharper disable once AccessToDisposedClosure
                await using var registration = timeout.Token.Register(() => RecordingCameraProcess.Kill(process));
                var listing = ReadListingAsync(process.StandardError, format, devices, timeout.Token);
                var output = RecordingCameraProcess.DrainAsync(process.StandardOutput, CancellationToken.None);
                try
                {
                    await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                    var detail = await listing.ConfigureAwait(false);
                    return new(devices, devices.Count == 0 ? detail : null);
                }
                finally
                {
                    RecordingCameraProcess.Kill(process);
                    await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                    try
                    {
                        await listing.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (timeout.IsCancellationRequested)
                    {
                    }

                    await output.ConfigureAwait(false);
                }
            }
            catch (Exception error) when (error is IOException or Win32Exception)
            {
                return new([], error.Message);
            }
        }

        private static RecordingCameraCatalogResult ListLinux()
        {
            const string root = "/sys/class/video4linux";
            if (!Directory.Exists(root)) return new([], null);
            var devices = new List<RecordingCameraDevice>();
            foreach (var directory in Directory.EnumerateDirectories(root).Order(StringComparer.Ordinal))
            {
                var node = Path.Combine("/dev", Path.GetFileName(directory));
                var name = File.ReadAllText(Path.Combine(directory, "name")).Trim();
                devices.Add(new(node, name, "v4l2", node, devices.Count));
            }

            return new(devices, null);
        }

        private static async Task<string?> ReadListingAsync(StreamReader reader, string format,
            List<RecordingCameraDevice> devices, CancellationToken token)
        {
            var video = format == "dshow";
            var expectingAlternative = false;
            string? failure = null;
            while (await reader.ReadLineAsync(token).ConfigureAwait(false) is { } line)
            {
                if (line.Contains("Unknown input format", StringComparison.Ordinal)) failure = line;
                if (format == "dshow")
                {
                    var match = DirectShowVideo().Match(line);
                    if (match.Success)
                    {
                        var name = match.Groups[1].Value;
                        devices.Add(new(name, name, format, "video=" + name, devices.Count));
                        expectingAlternative = true;
                        continue;
                    }

                    if (line.Contains("(audio)", StringComparison.Ordinal)) expectingAlternative = false;
                    match = DirectShowAlternative().Match(line);
                    if (match.Success && expectingAlternative)
                    {
                        var id = match.Groups[1].Value;
                        devices[^1] = devices[^1] with { Id = id, Input = "video=" + id };
                        expectingAlternative = false;
                    }
                }
                else
                {
                    if (line.Contains("AVFoundation video devices:", StringComparison.Ordinal)) video = true;
                    if (line.Contains("AVFoundation audio devices:", StringComparison.Ordinal)) video = false;
                    var match = AvFoundationDevice().Match(line);
                    if (!video || !match.Success) continue;
                    var index = match.Groups[1].Value;
                    var name = match.Groups[2].Value;
                    if (name.StartsWith("Capture screen", StringComparison.Ordinal)) continue;
                    devices.Add(new(index, name, format, index + ":none", devices.Count));
                }
            }

            return failure;
        }

        [GeneratedRegex("""["]([^"]+)["] \(video\)""")]
        private static partial Regex DirectShowVideo();

        [GeneratedRegex("""Alternative name ["]([^"]+)["]""")]
        private static partial Regex DirectShowAlternative();

        [GeneratedRegex(@"\] \[(\d+)\] (.+)$")]
        private static partial Regex AvFoundationDevice();
    }
}

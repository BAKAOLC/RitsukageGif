using System.Text.Json;

namespace RitsukageGif.Desktop
{
    internal static class LinuxAudioCapture
    {
        public static IAudioCaptureSource Create(bool systemAudio, string? deviceId, CancellationToken token)
        {
            var executable = FfmpegEncoder.FindExecutable() ??
                             throw new FfmpegDependencyException(UiText.Get("FfmpegMissing"));
            var devices = EnumerateDevices(systemAudio, token);
            var device = string.IsNullOrEmpty(deviceId)
                ? devices.FirstOrDefault(candidate => candidate.IsDefault)
                : devices.FirstOrDefault(candidate => candidate.Id == deviceId);
            if (device is null || !device.Available) throw new IOException(UiText.Get("VideoAudioDeviceMissing"));
            return new ProcessAudioCapture(ProcessAudioCapture.StartInfo(executable,
            [
                "-hide_banner", "-loglevel", "error", "-nostdin", "-f", "pulse", "-i", device.Id,
                "-vn", "-ac", "2", "-ar", "48000", "-c:a", "pcm_f32le", "-f", "f32le", "pipe:1",
            ]), true);
        }

        public static IReadOnlyList<AudioCaptureDevice> EnumerateDevices(bool systemAudio,
            CancellationToken token = default)
        {
            var defaultName = ProcessAudioCapture.Query("pactl", token, false,
                systemAudio ? "get-default-sink" : "get-default-source").Trim();
            var sources = ProcessAudioCapture.Query("pactl", token, false, "--format=json", "list", "sources");
            var sinks = systemAudio
                ? ProcessAudioCapture.Query("pactl", token, false, "--format=json", "list", "sinks")
                : null;
            return ParseDevices(sources, sinks, defaultName, systemAudio);
        }

        private static List<AudioCaptureDevice> ParseDevices(string sources, string? sinks,
            string defaultName, bool systemAudio)
        {
            string? monitor = null;
            if (sinks is not null)
            {
                using var outputs = JsonDocument.Parse(sinks);
                foreach (var sink in outputs.RootElement.EnumerateArray())
                    if (sink.GetProperty("name").GetString() == defaultName)
                        monitor = sink.GetProperty("monitor_source").GetString();
            }

            using var document = JsonDocument.Parse(sources);
            var devices = new List<AudioCaptureDevice>();
            // ReSharper disable once ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator
            foreach (var source in document.RootElement.EnumerateArray())
            {
                var monitorOfSink = source.GetProperty("monitor_source");
                var isMonitor = monitorOfSink.ValueKind == JsonValueKind.String &&
                                !string.IsNullOrEmpty(monitorOfSink.GetString());
                if (systemAudio != isMonitor) continue;
                var id = source.GetProperty("name").GetString()!;
                var description = source.GetProperty("description").GetString() ?? id;
                devices.Add(new(id, description, IsDefault: id == (systemAudio ? monitor : defaultName)));
            }

            return devices;
        }
    }
}

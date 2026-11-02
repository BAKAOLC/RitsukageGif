using System.Text.Json;

namespace RitsukageGif.Desktop
{
    internal static class MacAudioCapture
    {
        internal static string Executable => Path.Combine(AppContext.BaseDirectory, "RitsukageGif.Audio");

        public static IAudioCaptureSource Create(bool systemAudio, string? deviceId, CancellationToken token)
        {
            EnsureHelper();
            ProcessAudioCapture.Query(Executable, token, true,
                systemAudio ? "authorize-system" : "authorize-microphone");
            return new ProcessAudioCapture(ProcessAudioCapture.StartInfo(Executable,
                [systemAudio ? "system" : "microphone", deviceId ?? ""]));
        }

        public static IReadOnlyList<AudioCaptureDevice> EnumerateDevices(bool systemAudio)
        {
            EnsureHelper();
            using var document = JsonDocument.Parse(ProcessAudioCapture.Query(Executable,
                systemAudio ? "list-system" : "list-microphones"));
            return
            [
                .. document.RootElement.EnumerateArray().Select(device => new AudioCaptureDevice(
                    device.GetProperty("id").GetString()!, device.GetProperty("name").GetString()!,
                    IsDefault: device.GetProperty("isDefault").GetBoolean())),
            ];
        }

        private static void EnsureHelper()
        {
            if (!File.Exists(Executable))
                throw new FileNotFoundException(UiText.Get("CaptureHelperMissing"), Executable);
        }
    }
}

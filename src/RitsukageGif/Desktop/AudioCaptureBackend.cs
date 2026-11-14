namespace RitsukageGif.Desktop
{
    internal static class AudioCaptureBackend
    {
        public static bool IsSupported => OperatingSystem.IsWindows() || OperatingSystem.IsLinux() ||
                                          (OperatingSystem.IsMacOSVersionAtLeast(13) &&
                                           File.Exists(MacAudioCapture.Executable));

        public static bool IsAvailable(VideoRecordingCapabilities capabilities)
        {
            return IsSupported && (!OperatingSystem.IsLinux() || capabilities.AudioInputFormats.Contains("pulse"));
        }

        public static IAudioCaptureSource Create(bool systemAudio, string? deviceId, CancellationToken token = default)
        {
            if (OperatingSystem.IsWindows()) return WindowsAudioCapture.Create(systemAudio, deviceId);
            if (OperatingSystem.IsLinux()) return LinuxAudioCapture.Create(systemAudio, deviceId, token);
            if (OperatingSystem.IsMacOSVersionAtLeast(13)) return MacAudioCapture.Create(systemAudio, deviceId, token);
            throw new PlatformNotSupportedException(UiText.Get("VideoAudioUnavailable"));
        }

        public static IReadOnlyList<AudioCaptureDevice> EnumerateDevices(bool systemAudio)
        {
            return EnumerateDevices(systemAudio, CancellationToken.None);
        }

        internal static IReadOnlyList<AudioCaptureDevice> EnumerateDevices(bool systemAudio, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (OperatingSystem.IsWindows()) return WindowsAudioCapture.EnumerateDevices(systemAudio);
            if (OperatingSystem.IsLinux()) return LinuxAudioCapture.EnumerateDevices(systemAudio, token);
            if (OperatingSystem.IsMacOSVersionAtLeast(13)) return MacAudioCapture.EnumerateDevices(systemAudio, token);
            throw new PlatformNotSupportedException(UiText.Get("VideoAudioUnavailable"));
        }
    }
}

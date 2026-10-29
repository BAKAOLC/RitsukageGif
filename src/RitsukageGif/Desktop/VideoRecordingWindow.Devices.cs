namespace RitsukageGif.Desktop
{
    internal sealed partial class VideoRecordingWindow
    {
        // ReSharper disable once AutoPropertyCanBeMadeGetOnly.Global, MemberCanBePrivate.Global
        internal Func<bool, IReadOnlyList<AudioCaptureDevice>> AudioDeviceEnumerator { get; init; } =
            WindowsAudioCapture.EnumerateDevices;

        private async Task RefreshAudioDevicesAsync(bool systemAudio, string? selectedId)
        {
            List<AudioCaptureDevice> devices = [];
            string? error = null;
            try
            {
                var available = await Task.Run(() => AudioDeviceEnumerator(systemAudio),
                    _lifetime.Token);
                devices = WithDefaultDevice(available, selectedId);
            }
            catch (OperationCanceledException) when (_closed)
            {
                return;
            }
            catch (Exception exception)
            {
                error = ApplicationHost.VideoErrorMessage(exception);
            }

            if (_closed) return;
            var control = systemAudio ? _systemDevice : _microphoneDevice;
            if (systemAudio)
            {
                _systemDevices = devices;
                _systemDeviceError = error;
            }
            else
            {
                _microphoneDevices = devices;
                _microphoneDeviceError = error;
            }

            control.ItemsSource = devices.Select(device => device.Name).ToArray();
            control.SelectedIndex = Math.Max(0, devices.FindIndex(device => device.Id == selectedId));
        }
    }
}

namespace RitsukageGif.Desktop
{
    internal static class ScreenshotRecordingPreferenceAdapter
    {
        internal static RecordingCameraOptions RestoreCamera(ScreenshotRecordingCameraPreferences preferences,
            IReadOnlyList<RecordingCameraDevice> devices)
        {
            return new()
            {
                Enabled = preferences.Enabled,
                Primary = RestoreSlot(preferences.Primary, devices, devices.Count == 1 ? devices[0] : null),
                Secondary = RestoreSlot(preferences.Secondary, devices, null),
            };
        }

        internal static ScreenshotRecordingCameraPreferences CaptureCamera(RecordingCameraOptions options,
            ScreenshotRecordingCameraPreferences previous, bool devicesEdited)
        {
            return new()
            {
                Enabled = options.Enabled,
                Primary = CaptureSlot(options.Primary, previous.Primary, devicesEdited),
                Secondary = CaptureSlot(options.Secondary, previous.Secondary, devicesEdited),
            };
        }

        private static RecordingCameraSlot RestoreSlot(ScreenshotRecordingCameraSlotPreferences preferences,
            IReadOnlyList<RecordingCameraDevice> devices, RecordingCameraDevice? defaultDevice)
        {
            return new()
            {
                Device = preferences.DeviceId is null
                    ? defaultDevice
                    : devices.FirstOrDefault(device => device.Id == preferences.DeviceId),
                Shape = preferences.Shape,
                Size = preferences.Size,
                BorderColor = new(preferences.BorderColor),
                BorderWidth = preferences.BorderWidth,
                Rotation = preferences.Rotation,
                Mirrored = preferences.Mirrored,
            };
        }

        private static ScreenshotRecordingCameraSlotPreferences CaptureSlot(RecordingCameraSlot options,
            ScreenshotRecordingCameraSlotPreferences previous, bool devicesEdited)
        {
            return new()
            {
                DeviceId = devicesEdited ? options.Device?.Id : previous.DeviceId ?? options.Device?.Id,
                Shape = options.Shape,
                Size = options.Size,
                BorderColor = 0xFF000000u | ((uint)options.BorderColor.Red << 16) |
                              ((uint)options.BorderColor.Green << 8) | options.BorderColor.Blue,
                BorderWidth = options.BorderWidth,
                Rotation = options.Rotation,
                Mirrored = options.Mirrored,
            };
        }
    }
}

namespace RitsukageGif.Desktop
{
    public sealed record ScreenshotRecordingCameraSlotPreferences
    {
        public string? DeviceId { get; set; }
        public RecordingCameraShape Shape { get; set; }
        public int Size { get; set; } = 300;
        public uint BorderColor { get; set; } = 0xFFFFFFFF;
        public int BorderWidth { get; set; } = 2;
        public int Rotation { get; set; }
        public bool Mirrored { get; set; }

        public ScreenshotRecordingCameraSlotPreferences Normalize()
        {
            return this with
            {
                DeviceId = string.IsNullOrWhiteSpace(DeviceId) ? null : DeviceId.Trim(),
                Shape = Enum.IsDefined(Shape) ? Shape : RecordingCameraShape.Rectangle,
                Size = Math.Clamp(Size, 1, 480),
                BorderColor = BorderColor | 0xFF000000,
                BorderWidth = Math.Clamp(BorderWidth, 0, 10),
                Rotation = Rotation is 0 or 90 or 180 or 270 ? Rotation : 0,
            };
        }
    }
}

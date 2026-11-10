namespace RitsukageGif.Desktop
{
    public sealed record ScreenshotRecordingCameraPreferences
    {
        public bool Enabled { get; set; }
        public ScreenshotRecordingCameraSlotPreferences Primary { get; set; } = new();
        public ScreenshotRecordingCameraSlotPreferences Secondary { get; set; } = new();

        public ScreenshotRecordingCameraPreferences Normalize()
        {
            return this with
            {
                // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
                Primary = (Primary ?? new()).Normalize(),
                // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
                Secondary = (Secondary ?? new()).Normalize(),
            };
        }
    }
}

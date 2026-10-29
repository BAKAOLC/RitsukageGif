namespace RitsukageGif.Desktop
{
    public sealed record ScreenshotClipboardPreferences
    {
        public ScreenshotClipboardMode Mode { get; set; }
        public ScreenshotClipboardFileFormat FileFormat { get; set; }
        public ScreenshotOutputSize OutputSize { get; set; } = new();
        public int HdpiTargetPercent { get; set; }
        public bool PasteAfterCopy { get; set; }

        public ScreenshotClipboardPreferences Normalize()
        {
            return new()
            {
                Mode = Enum.IsDefined(Mode) ? Mode : ScreenshotClipboardMode.Image,
                FileFormat = Enum.IsDefined(FileFormat) ? FileFormat : ScreenshotClipboardFileFormat.Png,
                // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
                OutputSize = (OutputSize ?? new()).Normalize(),
                HdpiTargetPercent = HdpiTargetPercent is 100 or 150 or 200 or 250 or 300 ? HdpiTargetPercent : 0,
                PasteAfterCopy = PasteAfterCopy,
            };
        }
    }
}

namespace RitsukageGif.Desktop
{
    public sealed record ScreenshotSavePreferences : ScreenshotSaveProfile
    {
        public ScreenshotSaveProfile Quick { get; set; } = new();
        public ScreenshotSaveProfile Automatic { get; set; } = new() { Notify = false };
        public ScreenshotAutoSaveTriggers AutoSaveTriggers { get; set; }
        public bool RememberExtension { get; set; } = true;
        public StaticImageFormat? LastFormat { get; set; }
        public string? LastExtension { get; set; }

        public new ScreenshotSavePreferences Normalize()
        {
            return (ScreenshotSavePreferences)base.Normalize() with
            {
                // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
                Quick = (Quick ?? new()).Normalize(),
                // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
                Automatic = (Automatic ?? new() { Notify = false }).Normalize(),
                AutoSaveTriggers = AutoSaveTriggers & ScreenshotAutoSaveTriggers.All,
                LastFormat = LastFormat is { } format && Enum.IsDefined(format) ? format : null,
                LastExtension = NormalizeExtension(LastExtension),
            };
        }

        public ScreenshotSaveProfile ManualProfile()
        {
            return this with
            {
                Format = RememberExtension && LastFormat is { } format && Enum.IsDefined(format) ? format : Format,
                PreferredExtension = RememberExtension ? NormalizeExtension(LastExtension) : null,
            };
        }
    }
}

namespace RitsukageGif.Desktop
{
    public sealed record ScreenshotSavePreferences : ScreenshotSaveProfile
    {
        public ScreenshotSaveProfile Quick { get; set; } = new();
        public ScreenshotSaveProfile Automatic { get; set; } = new() { Notify = false };
        public ScreenshotAutoSaveTriggers AutoSaveTriggers { get; set; }
        public bool AutoSaveEnabled { get; set; }
        public int? AutomaticCleanupMaxAgeDays { get; set; }
        public SaveNotificationAction QuickNotificationAction { get; set; }
        public bool RememberExtension { get; set; } = true;
        public StaticImageFormat? LastFormat { get; set; }
        public string? LastExtension { get; set; }

        public new ScreenshotSavePreferences Normalize()
        {
            return (ScreenshotSavePreferences)base.Normalize() with
            {
                // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
                Quick = (Quick ?? new()).Normalize() with
                {
                    Quality = Math.Clamp(Quality, 0, 100),
                    AfterSave = Enum.IsDefined(AfterSave) ? AfterSave : ScreenshotAfterSaveAction.None,
                },
                // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
                Automatic = (Automatic ?? new() { Notify = false }).Normalize() with
                {
                    Quality = Math.Clamp(Quality, 0, 100), AfterSave = ScreenshotAfterSaveAction.None,
                },
                AutoSaveTriggers = AutoSaveTriggers & ScreenshotAutoSaveTriggers.All,
                AutomaticCleanupMaxAgeDays = AutomaticCleanupMaxAgeDays is > 0 ? AutomaticCleanupMaxAgeDays : null,
                QuickNotificationAction = Enum.IsDefined(QuickNotificationAction)
                    ? QuickNotificationAction
                    : SaveNotificationAction.LocateFile,
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

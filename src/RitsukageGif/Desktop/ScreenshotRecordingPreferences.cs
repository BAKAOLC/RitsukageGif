namespace RitsukageGif.Desktop
{
    public sealed record ScreenshotRecordingPreferences
    {
        public int Fps { get; set; } = 24;
        public int DelaySeconds { get; set; }
        public bool Quick { get; set; }
        public RecordingReviewQuality QuickQuality { get; set; } = RecordingReviewQuality.Original;
        public bool SkipQuickReminder { get; set; }
        public string QuickLastPath { get; set; } = string.Empty;
        public bool AutoExitAfterEncoding { get; set; }
        public RecordingReviewFormat ReviewFormat { get; set; } = RecordingReviewFormat.Mp4;
        public RecordingReviewQuality ReviewQuality { get; set; } = RecordingReviewQuality.Standard;
        public bool SystemAudio { get; set; } = true;
        public bool Microphone { get; set; } = true;
        public bool RecordKeyboardActions { get; set; } = true;
        public bool RecordMouseActions { get; set; } = true;
        public ScreenshotRecordingCameraPreferences Camera { get; set; } = new();
        public RecordingInputEffects InputEffects { get; set; } = new();
        public RecordingProgressEffects ProgressEffects { get; set; } = new();
        public RecordingToolbarCaptureMode ToolbarCaptureMode { get; set; }

        private static string NormalizeQuickLastPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return string.Empty;
            try
            {
                return Path.GetFullPath(path);
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return string.Empty;
            }
        }

        public ScreenshotRecordingPreferences Normalize()
        {
            return this with
            {
                Fps = Fps is 5 or 16 or 24 or 30 or 60 ? Fps : 24,
                DelaySeconds = DelaySeconds is 0 or 1 or 2 or 3 or 5 or 10 ? DelaySeconds : 0,
                QuickQuality = Enum.IsDefined(QuickQuality) ? QuickQuality : RecordingReviewQuality.Original,
                QuickLastPath = NormalizeQuickLastPath(QuickLastPath),
                ReviewFormat = Enum.IsDefined(ReviewFormat) ? ReviewFormat : RecordingReviewFormat.Mp4,
                ReviewQuality = Enum.IsDefined(ReviewQuality) ? ReviewQuality : RecordingReviewQuality.Standard,
                InputEffects = InputEffects.Normalize(),
                ProgressEffects = ProgressEffects.Normalize(),
                ToolbarCaptureMode = Enum.IsDefined(ToolbarCaptureMode)
                    ? ToolbarCaptureMode
                    : RecordingToolbarCaptureMode.Ask,
                // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
                Camera = (Camera ?? new()).Normalize(),
            };
        }
    }
}

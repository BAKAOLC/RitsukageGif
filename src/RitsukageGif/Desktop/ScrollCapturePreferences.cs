namespace RitsukageGif.Desktop
{
    public sealed record ScrollCapturePreferences
    {
        public ScrollCaptureInitialAction InitialAction { get; set; } =
            ScrollCaptureInitialAction.Vertical;

        public bool RespondShortcuts { get; set; } = true;
        public bool AutoCropOnReverse { get; set; }
        public bool SkipClearConfirmation { get; set; }
        public bool SkipFileOnlyWarning { get; set; }
        public bool SkipMatchFailureWarning { get; set; }

        internal bool IsWarningSuppressed(ScrollCaptureWarningKind kind)
        {
            return kind == ScrollCaptureWarningKind.FileOnly ? SkipFileOnlyWarning : SkipMatchFailureWarning;
        }

        internal ScrollCapturePreferences SuppressWarning(ScrollCaptureWarningKind kind)
        {
            return kind == ScrollCaptureWarningKind.FileOnly
                ? this with { SkipFileOnlyWarning = true }
                : this with { SkipMatchFailureWarning = true };
        }

        public ScrollCapturePreferences Normalize()
        {
            return this with
            {
                InitialAction = Enum.IsDefined(InitialAction)
                    ? InitialAction
                    : ScrollCaptureInitialAction.Vertical,
            };
        }
    }
}

namespace RitsukageGif.Desktop
{
    public sealed record ScrollCapturePreferences
    {
        public ScrollCaptureInitialAction InitialAction { get; set; } =
            ScrollCaptureInitialAction.DoNotStartAutomatically;

        public bool RespondShortcuts { get; set; } = true;
        public bool AutoCropOnReverse { get; set; }
        public bool SkipClearConfirmation { get; set; }

        public ScrollCapturePreferences Normalize()
        {
            return this with
            {
                InitialAction = Enum.IsDefined(InitialAction)
                    ? InitialAction
                    : ScrollCaptureInitialAction.DoNotStartAutomatically,
            };
        }
    }
}

namespace RitsukageGif.Desktop
{
    public sealed record ScrollCapturePreferences
    {
        public ScrollCaptureAxis InitialAxis { get; set; } = ScrollCaptureAxis.Vertical;
        public bool RespondShortcuts { get; set; } = true;
        public bool AutoCropOnReverse { get; set; }

        public ScrollCapturePreferences Normalize()
        {
            return this with { InitialAxis = Enum.IsDefined(InitialAxis) ? InitialAxis : ScrollCaptureAxis.Vertical };
        }
    }
}

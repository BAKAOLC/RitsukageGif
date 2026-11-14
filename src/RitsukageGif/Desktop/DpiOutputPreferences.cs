namespace RitsukageGif.Desktop
{
    public sealed record DpiOutputPreferences
    {
        public bool LogicalPixels { get; init; }
        public bool? ScreenshotSave { get; init; }
        public bool? ScreenshotCopy { get; init; }
        public bool? LongScreenshotSave { get; init; }
        public bool? LongScreenshotCopy { get; init; }
        public bool? QuickRecording { get; init; }
        public bool? ScreenshotRecording { get; init; }
        public bool? VideoRecording { get; init; }

        internal double ScaleFor(DpiOutputFeature feature, double dpi)
        {
            return dpi is > 1 and <= 16 && UsesLogicalPixels(feature) ? 1 / dpi : 1;
        }

        internal bool UsesLogicalPixels(DpiOutputFeature feature)
        {
            return OverrideFor(feature) ?? LogicalPixels;
        }

        internal bool? OverrideFor(DpiOutputFeature feature)
        {
            return feature switch
            {
                DpiOutputFeature.ScreenshotSave => ScreenshotSave,
                DpiOutputFeature.ScreenshotCopy => ScreenshotCopy,
                DpiOutputFeature.LongScreenshotSave => LongScreenshotSave,
                DpiOutputFeature.LongScreenshotCopy => LongScreenshotCopy,
                DpiOutputFeature.QuickRecording => QuickRecording,
                DpiOutputFeature.ScreenshotRecording => ScreenshotRecording,
                DpiOutputFeature.VideoRecording => VideoRecording,
                _ => throw new ArgumentOutOfRangeException(nameof(feature)),
            };
        }

        internal DpiOutputPreferences MergeChanges(DpiOutputPreferences baseline, DpiOutputPreferences edited)
        {
            return new()
            {
                LogicalPixels = baseline.LogicalPixels == edited.LogicalPixels ? LogicalPixels : edited.LogicalPixels,
                ScreenshotSave = baseline.ScreenshotSave == edited.ScreenshotSave
                    ? ScreenshotSave
                    : edited.ScreenshotSave,
                ScreenshotCopy = baseline.ScreenshotCopy == edited.ScreenshotCopy
                    ? ScreenshotCopy
                    : edited.ScreenshotCopy,
                LongScreenshotSave = baseline.LongScreenshotSave == edited.LongScreenshotSave
                    ? LongScreenshotSave
                    : edited.LongScreenshotSave,
                LongScreenshotCopy = baseline.LongScreenshotCopy == edited.LongScreenshotCopy
                    ? LongScreenshotCopy
                    : edited.LongScreenshotCopy,
                QuickRecording = baseline.QuickRecording == edited.QuickRecording
                    ? QuickRecording
                    : edited.QuickRecording,
                ScreenshotRecording = baseline.ScreenshotRecording == edited.ScreenshotRecording
                    ? ScreenshotRecording
                    : edited.ScreenshotRecording,
                VideoRecording = baseline.VideoRecording == edited.VideoRecording
                    ? VideoRecording
                    : edited.VideoRecording,
            };
        }
    }
}

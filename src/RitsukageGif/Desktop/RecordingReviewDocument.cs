namespace RitsukageGif.Desktop

{
    internal sealed record RecordingReviewDocument(
        IReadOnlyList<string> Paths,
        bool Video,
        PixelRect CaptureRegion,
        RecordingReviewOutputContext Output)

    {
        internal RecordingInputEffects InputEffects { get; init; } = new();
        internal RecordingProgressEffects ProgressEffects { get; init; } = new();
        internal Action<RecordingInputEffects>? InputEffectsChanged { get; init; }
        internal Action<RecordingProgressEffects>? ProgressEffectsChanged { get; init; }
        internal Action<bool>? CaptureCursorChanged { get; init; }
        internal RecordingReviewFormat Format { get; init; } = RecordingReviewFormat.Mp4;
        internal Action<RecordingReviewFormat>? FormatChanged { get; init; }
        internal RecordingReviewQuality Quality { get; init; } = RecordingReviewQuality.Standard;
        internal Action<RecordingReviewQuality>? QualityChanged { get; init; }
        internal OutputColorMode Colors { get; init; }
        internal bool AutoExitAfterEncoding { get; init; }
        internal Action<bool>? AutoExitAfterEncodingChanged { get; init; }
        internal bool SourceLost { get; init; }
        internal TimeSpan RecordedDuration { get; init; }
        internal long AcceptedCaptures { get; init; }
        internal int FramesPerSecond { get; init; } = 24;
        internal string? CaptureTitle { get; init; }
        internal string? CaptureProcess { get; init; }
    }
}

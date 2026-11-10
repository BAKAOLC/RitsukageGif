namespace RitsukageGif.Desktop
{
    internal sealed record RecordingReviewExportOptions(
        string Format,
        RecordingReviewQuality Quality,
        double Start,
        double End,
        double Speed = 1)
    {
        internal IReadOnlyList<RecordingAudioTrackMix> AudioMix { get; init; } = [];
        internal RecordingInputEffects InputEffects { get; init; } = new();
        internal RecordingProgressEffects ProgressEffects { get; init; } = new();
        internal bool Overwrite { get; init; }

        internal int EncodingQuality => Quality switch
        {
            RecordingReviewQuality.Economy => 70,
            RecordingReviewQuality.Standard => 85,
            RecordingReviewQuality.Original => 100,
            _ => throw new ArgumentOutOfRangeException(nameof(Quality)),
        };

        internal int VideoCrf => RecordingEncodingQuality.VideoCrf(Quality);

        internal int Colors => Quality switch
        {
            RecordingReviewQuality.Economy => 64,
            RecordingReviewQuality.Standard => 128,
            RecordingReviewQuality.Original => 255,
            _ => throw new ArgumentOutOfRangeException(nameof(Quality)),
        };

        internal bool Dither => Quality == RecordingReviewQuality.Original;
    }
}

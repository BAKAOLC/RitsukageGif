namespace RitsukageGif.Desktop
{
    internal static class RecordingEncodingQuality
    {
        internal static int VideoCrf(RecordingReviewQuality quality)
        {
            return quality switch
            {
                RecordingReviewQuality.Economy => 35,
                RecordingReviewQuality.Standard => 26,
                RecordingReviewQuality.Original => 17,
                _ => throw new ArgumentOutOfRangeException(nameof(quality)),
            };
        }
    }
}

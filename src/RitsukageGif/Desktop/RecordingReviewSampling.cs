namespace RitsukageGif.Desktop
{
    internal static class RecordingReviewSampling
    {
        private static long LogicalFrameCount(RecordingReviewDocument document)
        {
            return Math.Max(document.AcceptedCaptures,
                (long)Math.Floor((long)document.RecordedDuration.TotalMilliseconds * document.FramesPerSecond / 1000d +
                                 .5));
        }

        internal static long IndexAtTime(RecordingReviewDocument document, double position)
        {
            return Math.Clamp((long)Math.Floor(Math.BitIncrement(position * document.FramesPerSecond)), 0,
                LogicalFrameCount(document) - 1);
        }

        internal static double MediaPosition(RecordingReviewDocument document, double position)
        {
            return Range(document, position, position).Start / (double)document.FramesPerSecond;
        }

        internal static double DisplayPosition(RecordingReviewDocument document, double mediaPosition)
        {
            var last = LogicalFrameCount(document) - 1;
            return last == 0
                ? 0
                : IndexAtTime(document, mediaPosition) *
                (long)document.RecordedDuration.TotalMilliseconds / (double)last / 1000;
        }

        internal static RecordingReviewFrameRange Range(RecordingReviewDocument document, double start, double end)
        {
            var last = LogicalFrameCount(document) - 1;
            var duration = (long)document.RecordedDuration.TotalMilliseconds;
            return new(Math.Clamp((long)(start * 1000) * last / duration, 0, last),
                Math.Clamp((long)(end * 1000) * last / duration, 0, last), document.FramesPerSecond);
        }
    }
}

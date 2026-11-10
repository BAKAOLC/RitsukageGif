namespace RitsukageGif.Desktop
{
    internal sealed class RecordingReviewProgressSlice(IProgress<double>? progress, double start, double length)
        : IProgress<double>
    {
        public void Report(double value)
        {
            progress?.Report(start + length * Math.Clamp(value, 0, 1));
        }
    }
}

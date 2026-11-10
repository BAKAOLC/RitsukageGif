namespace RitsukageGif.Desktop
{
    internal sealed class RecordingReviewProgressValue : IProgress<double>
    {
        private double _value;
        internal double Value => Volatile.Read(ref _value);

        public void Report(double value)
        {
            Volatile.Write(ref _value, value);
        }
    }
}

namespace RitsukageGif.Desktop
{
    internal sealed class MultiRecordingProgressSink(Action<MultiRecordingProgress> report)
        : IProgress<MultiRecordingProgress>
    {
        public void Report(MultiRecordingProgress value)
        {
            report(value);
        }
    }
}

namespace RitsukageGif.Core
{
    public sealed partial class RecordingControl
    {
        private sealed class MediaTimeline
        {
            public long StartedAt = -1;
            public long Origin;
            public long LastSequence;
            public long AcceptedCaptures;
            public TimeSpan? Duration;
        }
    }
}

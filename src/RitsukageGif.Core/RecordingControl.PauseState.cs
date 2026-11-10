namespace RitsukageGif.Core
{
    public sealed partial class RecordingControl
    {
        private sealed class PauseState(long startedAt, long pausedTicks, TaskCompletionSource? resume, long generation)
        {
            public long StartedAt { get; } = startedAt;
            public long PausedTicks { get; } = pausedTicks;
            public TaskCompletionSource? Resume { get; } = resume;
            public long Generation { get; } = generation;
        }
    }
}

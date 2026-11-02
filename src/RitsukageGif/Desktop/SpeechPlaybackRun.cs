namespace RitsukageGif.Desktop
{
    internal sealed class SpeechPlaybackRun(CancellationToken token)
    {
        internal CancellationTokenSource Cancellation { get; } = CancellationTokenSource.CreateLinkedTokenSource(token);
        internal Task Completion { get; set; } = Task.CompletedTask;
    }
}

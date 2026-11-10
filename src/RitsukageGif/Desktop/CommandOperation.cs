namespace RitsukageGif.Desktop
{
    internal sealed class CommandOperation(
        string id,
        string kind,
        string? source,
        CancellationTokenSource cancellation) : IDisposable
    {
        private string Id { get; } = id;
        private string Kind { get; } = kind;
        private string? Source { get; } = source;
        private DateTimeOffset Started { get; } = DateTimeOffset.UtcNow;
        internal CancellationTokenSource Cancellation { get; } = cancellation;
        internal bool ExitRequested { get; set; }
        internal TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal CommandOperationInfo Info => new(Id, Kind,
            Cancellation.IsCancellationRequested ? "cancelling" : "running", Source, Started);

        public void Dispose()
        {
            Cancellation.Dispose();
        }
    }
}

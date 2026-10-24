namespace RitsukageGif.Desktop
{
    internal sealed partial class MediaResource
    {
        private sealed class Download
        {
            public CancellationTokenSource Cancellation { get; } = new();
            public Task<Entry> Task { get; set; } = null!;
            public int Waiters { get; set; }
        }
    }
}

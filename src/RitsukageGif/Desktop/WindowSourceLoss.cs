namespace RitsukageGif.Desktop
{
    internal sealed class WindowSourceLoss(CancellationTokenSource stop, Action notify)
    {
        private int _lost;
        internal bool WasLost => Volatile.Read(ref _lost) != 0;

        internal void Report()
        {
            if (Interlocked.Exchange(ref _lost, 1) != 0) return;
            stop.Cancel();
            notify();
        }
    }
}

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private bool _fatalFailure;

        internal void SuspendAfterFailure()
        {
            _fatalFailure = true;
            _closing = true;
            _hotkeys?.Dispose();
            _hotkeys = null;
            _stop?.Cancel();
            _abort?.Cancel();
            CancelSelection();
        }
    }
}

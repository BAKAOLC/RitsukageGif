namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotRecordingController
    {
        private Task _startTask = Task.CompletedTask;
        private CancellationTokenSource? _countdown;
        internal int? CountdownRemaining { get; private set; }
        internal bool IsCountingDown => _countdown is not null;

        private async Task<bool> WaitForCountdownAsync(int seconds, CancellationToken token)
        {
            if (seconds == 0) return true;
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            _countdown = cancellation;
            try
            {
                for (var remaining = seconds; remaining > 0; remaining--)
                {
                    CountdownRemaining = remaining;
                    Notify();
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellation.Token);
                }

                return true;
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                return false;
            }
            finally
            {
                _countdown = null;
                CountdownRemaining = null;
                Notify();
            }
        }
    }
}

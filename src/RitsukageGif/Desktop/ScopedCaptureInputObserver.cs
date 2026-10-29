namespace RitsukageGif.Desktop
{
    internal static class ScopedCaptureInputObserver
    {
        internal static bool IsSupported => OperatingSystem.IsWindows();

        internal static async Task<IDisposable> StartAsync(Action released, Action cancelled,
            Action<Exception> failed, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(released);
            ArgumentNullException.ThrowIfNull(cancelled);
            ArgumentNullException.ThrowIfNull(failed);
            token.ThrowIfCancellationRequested();
            if (!IsSupported) throw new PlatformNotSupportedException();
            var subscription = new ScopedCaptureInputSubscription(released, cancelled, failed, token);
            subscription.Start();
            try
            {
                var ready = await subscription.Ready.ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                return ready;
            }
            catch
            {
                subscription.Dispose();
                await subscription.Completion.ConfigureAwait(false);
                throw;
            }
        }
    }
}

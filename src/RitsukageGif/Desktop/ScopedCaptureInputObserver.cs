namespace RitsukageGif.Desktop
{
    internal static class ScopedCaptureInputObserver
    {
        internal static bool IsSupported => RecordingInputObserver.UnavailableReason is null;

        internal static async Task<IAsyncDisposable> StartAsync(Action released, Action cancelled,
            Action<Exception> failed, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(released);
            ArgumentNullException.ThrowIfNull(cancelled);
            ArgumentNullException.ThrowIfNull(failed);
            token.ThrowIfCancellationRequested();
            if (!IsSupported) throw new PlatformNotSupportedException();
            if (!OperatingSystem.IsWindows())
                return await PortableCaptureInputSubscription.StartAsync(released, cancelled, failed, token)
                    .ConfigureAwait(false);
            var subscription = new ScopedCaptureInputSubscription(released, cancelled, failed, token);
            subscription.Start();
            try
            {
                await subscription.Ready.ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                return subscription;
            }
            catch
            {
                await subscription.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
    }
}

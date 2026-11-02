namespace RitsukageGif.Desktop
{
    internal sealed class NetworkRouteGeneration(
        NetworkPreferences preferences,
        string? password,
        bool allowAutoRedirect)
    {
        private readonly Lazy<Task<HttpMessageInvoker>> _invoker = new(async () =>
            new(await ApplicationHttp.CreateHandlerAsync(preferences, password, allowAutoRedirect)
                .ConfigureAwait(false)));

        private int _leases = 1;

        internal void Acquire()
        {
            Interlocked.Increment(ref _leases);
        }

        internal async Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken token)
        {
            var invoker = await _invoker.Value.WaitAsync(token).ConfigureAwait(false);
            return await invoker.SendAsync(message, token).ConfigureAwait(false);
        }

        internal void Release()
        {
            if (Interlocked.Decrement(ref _leases) != 0 || !_invoker.IsValueCreated) return;
            _ = _invoker.Value.ContinueWith(static task =>
            {
                if (task.IsCompletedSuccessfully) task.Result.Dispose();
                else _ = task.Exception;
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }
}

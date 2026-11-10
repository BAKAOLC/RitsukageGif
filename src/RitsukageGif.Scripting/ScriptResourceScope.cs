namespace RitsukageGif.Scripting
{
    public sealed class ScriptResourceScope : IAsyncDisposable
    {
        private readonly Lock _gate = new();
        private readonly List<object> _resources = [];
        private Task? _disposal;

        public ValueTask DisposeAsync()
        {
            lock (_gate)
            {
                if (_disposal is not null) return new(_disposal);
                var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _disposal = completion.Task;
                var resources = _resources.ToArray();
                _resources.Clear();
                _ = DisposeResourcesAsync(resources, completion);
                return new(_disposal);
            }
        }

        public T Own<T>(T resource) where T : class
        {
            ArgumentNullException.ThrowIfNull(resource);
            if (resource is not (IDisposable or IAsyncDisposable))
                throw new ArgumentException("A script resource must support disposal.", nameof(resource));
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposal is not null, this);
                _resources.Add(resource);
            }

            return resource;
        }

        private static async Task DisposeResourcesAsync(object[] resources, TaskCompletionSource completion)
        {
            List<Exception>? failures = null;
            for (var index = resources.Length - 1; index >= 0; index--)
                try
                {
                    if (resources[index] is IAsyncDisposable asynchronous)
                        await asynchronous.DisposeAsync().ConfigureAwait(false);
                    else ((IDisposable)resources[index]).Dispose();
                }
                catch (Exception error)
                {
                    (failures ??= []).Add(error);
                }

            if (failures is null) completion.TrySetResult();
            else completion.TrySetException(new AggregateException(failures));
        }
    }
}

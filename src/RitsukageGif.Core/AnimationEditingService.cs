namespace RitsukageGif.Core
{
    public sealed class AnimationEditingService(
        Func<string, CancellationToken, Task<AnimationEditDocument>>? open = null) : IAnimationEditingService
    {
        private readonly List<IAnimationDocument> _documents = [];
        private readonly SemaphoreSlim _gate = new(1, 1);
        private bool _disposed;

        public Task<IAnimationDocument> OpenAsync(string path, CancellationToken token = default)
        {
            return AddAsync(() => open is null
                ? Task.Run(() => AnimationEditDocument.Open(path, token), token)
                : open(path, token), token);
        }

        public Task<IAnimationDocument> CreateAsync(int width, int height, int delayMs = 100,
            CancellationToken token = default)
        {
            return AddAsync(() => Task.Run(() => AnimationEditDocument.Create(width, height, delayMs), token), token);
        }

        public async ValueTask DisposeAsync()
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_disposed) return;
                _disposed = true;
                List<Exception>? failures = null;
                for (var index = _documents.Count - 1; index >= 0; index--)
                    try
                    {
                        await _documents[index].DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception error)
                    {
                        (failures ??= []).Add(error);
                    }

                _documents.Clear();
                if (failures is not null) throw new AggregateException(failures);
            }
            finally
            {
                _gate.Release();
            }
        }

        private async Task<IAnimationDocument> AddAsync(Func<Task<AnimationEditDocument>> create,
            CancellationToken token)
        {
            await _gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                var document = await create().ConfigureAwait(false);
                try
                {
                    token.ThrowIfCancellationRequested();
                    var result = new AnimationEditingDocument(document);
                    _documents.Add(result);
                    return result;
                }
                catch
                {
                    document.Dispose();
                    throw;
                }
            }
            finally
            {
                _gate.Release();
            }
        }
    }
}

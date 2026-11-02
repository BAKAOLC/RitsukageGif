using System.Buffers;

namespace RitsukageGif.Desktop
{
    internal sealed class PinnedRichResourceStore : IAsyncDisposable
    {
        private readonly List<PinnedRichResourceAsset> _assets = [];
        private readonly Func<(string Path, IDisposable Owner)> _directoryFactory;
        private readonly HashSet<string> _files = new(StringComparer.Ordinal);
        private readonly Lock _gate = new();
        private readonly CancellationTokenSource _lifetime = new();
        private readonly ILogger _logger;
        private readonly long _memoryBudget;
        private bool _closing;
        private (string Path, IDisposable Owner)? _directory;
        private Task? _disposal;
        private TaskCompletionSource? _drained;
        private int _operations;
        private long _residentBytes;

        internal PinnedRichResourceStore(Func<(string Path, IDisposable Owner)> directoryFactory,
            long memoryBudget, long maximumResourceBytes, ILogger logger)
        {
            ArgumentNullException.ThrowIfNull(directoryFactory);
            ArgumentNullException.ThrowIfNull(logger);
            ArgumentOutOfRangeException.ThrowIfNegative(memoryBudget);
            if (maximumResourceBytes <= 0 || maximumResourceBytes > Array.MaxLength)
                throw new ArgumentOutOfRangeException(nameof(maximumResourceBytes));
            _directoryFactory = directoryFactory;
            _memoryBudget = memoryBudget;
            MaximumResourceBytes = maximumResourceBytes;
            _logger = logger;
        }

        internal long MaximumResourceBytes { get; }


        public ValueTask DisposeAsync()
        {
            Task wait;
            TaskCompletionSource completion;
            lock (_gate)
            {
                if (_disposal is not null) return new(_disposal);
                _closing = true;
                completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _disposal = completion.Task;
                wait = _operations == 0
                    ? Task.CompletedTask
                    : (_drained = new(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
            }

            _ = DisposeCoreAsync(wait, completion);
            return new(completion.Task);
        }

        internal async Task<PinnedRichResourceAsset> AdoptAsync(byte[] owned, CancellationToken token)
        {
            BeginOperation();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
            try
            {
                linked.Token.ThrowIfCancellationRequested();
                if (owned.LongLength > MaximumResourceBytes)
                    throw new InvalidDataException("The image resource exceeds its encoded-byte budget.");
                if (ReserveMemory(owned.LongLength))
                {
                    var admitted = false;
                    try
                    {
                        var asset = new PinnedRichResourceAsset(this, owned, owned.Length);
                        Admit(asset);
                        admitted = true;
                        return asset;
                    }
                    finally
                    {
                        if (!admitted) ReleaseMemory(owned.LongLength);
                    }
                }

                await using var stream = new MemoryStream(owned, false);
                return await SpillAsync(stream, ReadOnlyMemory<byte>.Empty, linked.Token).ConfigureAwait(false);
            }
            finally
            {
                EndOperation();
            }
        }

        internal async Task<PinnedRichResourceAsset> ImportAsync(Stream source, long? length,
            CancellationToken token)
        {
            BeginOperation();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
            long reservation = 0;
            try
            {
                token = linked.Token;
                token.ThrowIfCancellationRequested();
                if (length > MaximumResourceBytes)
                    throw new InvalidDataException("The image resource exceeds its encoded-byte budget.");
                if (length is >= 0 && ReserveMemory(length.Value))
                {
                    reservation = length.Value;
                    var bytes = new byte[(int)length.Value];
                    var offset = 0;
                    while (offset < bytes.Length)
                    {
                        var count = await source.ReadAsync(bytes.AsMemory(offset), token).ConfigureAwait(false);
                        if (count == 0) break;
                        offset += count;
                    }

                    var extra = new byte[1];
                    var tail = offset == bytes.Length
                        ? await source.ReadAsync(extra, token).ConfigureAwait(false)
                        : 0;
                    if (tail == 0)
                    {
                        var asset = new PinnedRichResourceAsset(this, bytes, offset);
                        Admit(asset);
                        reservation = 0;
                        return asset;
                    }

                    return await SpillAsync(source, bytes, token, extra[0]).ConfigureAwait(false);
                }

                return await SpillAsync(source, ReadOnlyMemory<byte>.Empty, token).ConfigureAwait(false);
            }
            finally
            {
                if (reservation != 0) ReleaseMemory(reservation);
                EndOperation();
            }
        }

        internal async ValueTask<ReadOnlyMemory<byte>> ReadAsync(PinnedRichResourceAsset asset,
            CancellationToken token)
        {
            BeginOperation();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
            try
            {
                return await asset.ReadStoredAsync(linked.Token).ConfigureAwait(false);
            }
            finally
            {
                EndOperation();
            }
        }

        private async Task<PinnedRichResourceAsset> SpillAsync(Stream source, ReadOnlyMemory<byte> prefix,
            CancellationToken token, byte? extra = null)
        {
            string path;
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_closing, this);
                _directory ??= _directoryFactory();
                if (!Path.IsPathFullyQualified(_directory.Value.Path))
                    throw new ArgumentException("The resource directory must be absolute.");
                path = Path.Combine(_directory.Value.Path, Guid.NewGuid().ToString("N") + ".asset");
            }

            var created = false;
            try
            {
                long length = prefix.Length + (extra is null ? 0 : 1);
                if (length > MaximumResourceBytes)
                    throw new InvalidDataException("The image resource exceeds its encoded-byte budget.");
                await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write,
                                 FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    created = true;
                    lock (_gate) _files.Add(path);
                    await output.WriteAsync(prefix, token).ConfigureAwait(false);
                    if (extra is { } value) await output.WriteAsync(new[] { value }, token).ConfigureAwait(false);
                    var buffer = ArrayPool<byte>.Shared.Rent(65536);
                    try
                    {
                        int count;
                        while ((count = await source.ReadAsync(buffer.AsMemory(0, 65536), token)
                                   .ConfigureAwait(false)) != 0)
                        {
                            length += count;
                            if (length > MaximumResourceBytes)
                                throw new InvalidDataException("The image resource exceeds its encoded-byte budget.");
                            await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                    }
                }

                token.ThrowIfCancellationRequested();
                var asset = new PinnedRichResourceAsset(this, path, length);
                Admit(asset);
                return asset;
            }
            catch
            {
                if (created) DeleteCreatedFile(path);
                throw;
            }
        }

        private bool ReserveMemory(long count)
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_closing, this);
                if (count > _memoryBudget - _residentBytes) return false;
                _residentBytes += count;
                return true;
            }
        }

        private void ReleaseMemory(long count)
        {
            lock (_gate) _residentBytes -= count;
        }

        private void Admit(PinnedRichResourceAsset asset)
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_closing, this);
                _assets.Add(asset);
            }
        }

        private void BeginOperation()
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_closing, this);
                _operations++;
            }
        }

        private void EndOperation()
        {
            TaskCompletionSource? drained = null;
            lock (_gate)
            {
                _operations--;
                if (_closing && _operations == 0) drained = _drained;
            }

            drained?.TrySetResult();
        }

        private void DeleteCreatedFile(string path)
        {
            try
            {
                File.Delete(path);
                lock (_gate) _files.Remove(path);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning("Rich image source cleanup was deferred. ErrorType={ErrorType}",
                    error.GetType().Name);
            }
        }

        private async Task DisposeCoreAsync(Task wait, TaskCompletionSource completion)
        {
            try
            {
                try
                {
                    await _lifetime.CancelAsync().ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    _logger.LogError("Rich image source cancellation failed. ErrorType={ErrorType}",
                        error.GetType().Name);
                }

                await wait.ConfigureAwait(false);
                foreach (var path in _files.ToArray()) DeleteCreatedFile(path);
                foreach (var asset in _assets) asset.ReleaseSource();
                _assets.Clear();
                lock (_gate) _residentBytes = 0;
                _directory?.Owner.Dispose();
                _directory = null;
            }
            catch (Exception error)
            {
                _logger.LogError("Rich image source store shutdown failed. ErrorType={ErrorType}",
                    error.GetType().Name);
            }
            finally
            {
                _lifetime.Dispose();
                completion.TrySetResult();
            }
        }
    }
}

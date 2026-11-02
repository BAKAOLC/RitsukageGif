using Avalonia.Media.Imaging;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedRichResourceLoader : IDisposable, IAsyncDisposable
    {
        private readonly Func<ReadOnlyMemory<byte>, CancellationToken, Task<Bitmap>> _decode;
        private readonly long _decodedBudget;
        private readonly Dictionary<string, PinnedRichResourceEntry> _entries = new(StringComparer.Ordinal);
        private readonly Lock _gate = new();
        private readonly HttpClient _http;
        private readonly LinkedList<PinnedRichResourceEntry> _idle = [];
        private readonly CancellationTokenSource _lifetime = new();
        private readonly ILogger _logger;
        private readonly bool _ownsHttp;
        private readonly SemaphoreSlim _parallel;
        private readonly HashSet<Task> _pending = [];
        private readonly PinnedRichResourceStore _store;
        private readonly CancellationToken _token;
        private long _decodedBytes;
        private Task? _disposal;
        private bool _disposed;
        private long _nextId;

        internal PinnedRichResourceLoader(HttpClient http,
            Func<ReadOnlyMemory<byte>, CancellationToken, Task<Bitmap>> decode,
            PinnedRichResourceStore store, long decodedCacheBudget, int maximumParallelLoads,
            ILogger logger, bool ownsHttp = true)
        {
            ArgumentNullException.ThrowIfNull(http);
            ArgumentNullException.ThrowIfNull(decode);
            ArgumentNullException.ThrowIfNull(store);
            ArgumentNullException.ThrowIfNull(logger);
            ArgumentOutOfRangeException.ThrowIfNegative(decodedCacheBudget);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumParallelLoads);
            _http = http;
            _ownsHttp = ownsHttp;
            _decode = decode;
            _store = store;
            _decodedBudget = decodedCacheBudget;
            _parallel = new(maximumParallelLoads, maximumParallelLoads);
            _logger = logger;
            _token = _lifetime.Token;
        }

        public ValueTask DisposeAsync()
        {
            return new(StartDispose());
        }

        public void Dispose()
        {
            _ = StartDispose();
        }

        internal ValueTask<PinnedRichResource> AcquireAsync(string source, Uri? baseUri,
            CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (!Resolve(source, baseUri, out var key, out var address))
                return new(new PinnedRichResource(null, "The image resource cannot be resolved.", null));
            PinnedRichResourceEntry entry;
            TaskCompletionSource completion;
            var start = false;
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (!_entries.TryGetValue(key, out entry!))
                {
                    entry = new(key, address, ++_nextId);
                    _entries.Add(key, entry);
                }

                if (entry.Bitmap is not null || entry.Error is not null) return new(LeaseLocked(entry));
                if (entry.Pending is null)
                {
                    entry.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    _pending.Add(entry.Pending.Task);
                    start = true;
                }

                completion = entry.Pending;
                entry.Waiters++;
            }

            if (start) _ = Task.Run(() => LoadAsync(entry, completion), CancellationToken.None);
            return AwaitLeaseAsync(entry, completion.Task, token);
        }

        private async ValueTask<PinnedRichResource> AwaitLeaseAsync(PinnedRichResourceEntry entry,
            Task pending, CancellationToken token)
        {
            try
            {
                await pending.WaitAsync(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                _token.ThrowIfCancellationRequested();
                lock (_gate)
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    return LeaseLocked(entry);
                }
            }
            finally
            {
                List<Bitmap>? retired = null;
                lock (_gate)
                {
                    entry.Waiters--;
                    MakeIdleLocked(entry, ref retired);
                }

                DisposeBitmaps(retired);
            }
        }

        private PinnedRichResource LeaseLocked(PinnedRichResourceEntry entry)
        {
            if (entry.Bitmap is null) return new(null, entry.Error ?? "The image resource cannot be loaded.", null);
            if (entry.IdleNode is { } node)
            {
                _idle.Remove(node);
                entry.IdleNode = null;
            }

            entry.Leases++;
            return new(entry.Bitmap, null, () => Release(entry));
        }

        private void Release(PinnedRichResourceEntry entry)
        {
            List<Bitmap>? retired = null;
            lock (_gate)
            {
                entry.Leases--;
                MakeIdleLocked(entry, ref retired);
            }

            DisposeBitmaps(retired);
        }

        private void MakeIdleLocked(PinnedRichResourceEntry entry, ref List<Bitmap>? retired)
        {
            if (entry is { Bitmap: not null, Leases: 0, Waiters: 0 })
            {
                if (_disposed)
                {
                    RetireLocked(entry, ref retired);
                }
                else
                {
                    if (entry.IdleNode is not null) _idle.Remove(entry.IdleNode);
                    entry.IdleNode = _idle.AddFirst(entry);
                }
            }

            while (_decodedBytes > _decodedBudget && _idle.Last is { } last)
                RetireLocked(last.Value, ref retired);
        }

        private void RetireLocked(PinnedRichResourceEntry entry, ref List<Bitmap>? retired)
        {
            if (entry.IdleNode is { } node) _idle.Remove(node);
            entry.IdleNode = null;
            if (entry.Bitmap is not { } bitmap) return;
            entry.Bitmap = null;
            _decodedBytes -= entry.DecodedBytes;
            entry.DecodedBytes = 0;
            (retired ??= []).Add(bitmap);
        }

        private async Task LoadAsync(PinnedRichResourceEntry entry, TaskCompletionSource completion)
        {
            Bitmap? bitmap = null;
            string? failure = null;
            var canceled = false;
            var entered = false;
            try
            {
                await _parallel.WaitAsync(_token).ConfigureAwait(false);
                entered = true;
                entry.Asset ??= await ReadSourceAsync(entry, _token).ConfigureAwait(false);
                var encoded = await entry.Asset.ReadAsync(_token).ConfigureAwait(false);
                _token.ThrowIfCancellationRequested();
                bitmap = await _decode(encoded, _token).ConfigureAwait(false);
                _token.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (_token.IsCancellationRequested)
            {
                canceled = true;
            }
            catch (Exception error)
            {
                failure = "The image resource cannot be loaded.";
                _logger.LogWarning(
                    "Rich image resource load failed. ResourceId={ResourceId} Scheme={Scheme} ErrorType={ErrorType}",
                    entry.Id, entry.Scheme, error.GetType().Name);
            }
            finally
            {
                if (entered) _parallel.Release();
            }

            List<Bitmap>? retired = null;
            lock (_gate)
            {
                if (!_disposed && !canceled)
                {
                    if (bitmap is not null)
                    {
                        entry.Bitmap = bitmap;
                        entry.DecodedBytes = (long)bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4;
                        _decodedBytes += entry.DecodedBytes;
                        bitmap = null;
                    }

                    entry.Error = failure;
                    MakeIdleLocked(entry, ref retired);
                }

                entry.Pending = null;
            }

            bitmap?.Dispose();
            DisposeBitmaps(retired);
            lock (_gate) _pending.Remove(completion.Task);
            if (_disposed || canceled) completion.TrySetCanceled(_token);
            else completion.TrySetResult();
        }

        private async Task<PinnedRichResourceAsset> ReadSourceAsync(PinnedRichResourceEntry entry,
            CancellationToken token)
        {
            if (entry.Address is null)
                return await _store.AdoptAsync(ReadData(entry.Key, _store.MaximumResourceBytes, token), token)
                    .ConfigureAwait(false);
            if (entry.Address.IsFile)
            {
                await using var input = new FileStream(entry.Address.LocalPath, FileMode.Open, FileAccess.Read,
                    FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
                return await _store.ImportAsync(input, input.Length, token).ConfigureAwait(false);
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, entry.Address);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            return await _store.ImportAsync(stream, response.Content.Headers.ContentLength, token)
                .ConfigureAwait(false);
        }

        private static bool Resolve(string source, Uri? baseUri, out string key, out Uri? address)
        {
            key = source;
            address = null;
            if (source.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return true;
            if (!Uri.TryCreate(source, UriKind.Absolute, out address) &&
                (baseUri is not { IsAbsoluteUri: true } || !Uri.TryCreate(baseUri, source, out address))) return false;
            if (address.Scheme is not ("file" or "http" or "https")) return false;
            key = address.AbsoluteUri;
            return true;
        }

        private static void DisposeBitmaps(List<Bitmap>? bitmaps)
        {
            if (bitmaps is null) return;
            foreach (var bitmap in bitmaps) bitmap.Dispose();
        }

        private Task StartDispose()
        {
            Task[] pending;
            TaskCompletionSource completion;
            List<Bitmap>? retired = null;
            lock (_gate)
            {
                if (_disposal is not null) return _disposal;
                _disposed = true;
                completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _disposal = completion.Task;
                pending = [.. _pending];
                foreach (var entry in _entries.Values)
                    if (entry.Leases == 0)
                    {
                        RetireLocked(entry, ref retired);
                    }
                    else if (entry.IdleNode is { } node)
                    {
                        _idle.Remove(node);
                        entry.IdleNode = null;
                    }

                _entries.Clear();
            }

            DisposeBitmaps(retired);
            _ = DisposeCoreAsync(pending, completion);
            return completion.Task;
        }

        private async Task DisposeCoreAsync(Task[] pending, TaskCompletionSource completion)
        {
            try
            {
                try
                {
                    await _lifetime.CancelAsync().ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    _logger.LogError("Rich image request cancellation failed. ErrorType={ErrorType}",
                        error.GetType().Name);
                }

                try
                {
                    await Task.WhenAll(pending).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }

                await _store.DisposeAsync().ConfigureAwait(false);
                if (_ownsHttp) _http.Dispose();
                _parallel.Dispose();
                _lifetime.Dispose();
            }
            catch (Exception error)
            {
                _logger.LogError("Rich image resource shutdown failed. ErrorType={ErrorType}", error.GetType().Name);
            }
            finally
            {
                completion.TrySetResult();
            }
        }
    }
}

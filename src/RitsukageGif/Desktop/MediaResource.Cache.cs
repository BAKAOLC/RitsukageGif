namespace RitsukageGif.Desktop
{
    internal sealed partial class MediaResource
    {
        private const long MaximumCacheBytes = 128L * 1024 * 1024;
        private const int MaximumCacheEntries = 16;
        private static readonly Lock Gate = new();
        private static readonly Dictionary<string, Entry> Cache = new(StringComparer.Ordinal);
        private static readonly Dictionary<string, Download> Downloads = new(StringComparer.Ordinal);

        private static readonly Timer ExpirationTimer =
            new(_ => TrimCache(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

        private static long _cacheBytes;

        static MediaResource()
        {
            AppDomain.CurrentDomain.ProcessExit += (_, _) => ClearCache();
        }

        private static async Task<MediaResource> OpenRemoteAsync(Uri uri, CancellationToken cancellationToken)
        {
            // Fragments are not sent to the server and must not create duplicate downloads.
            var key = uri.GetComponents(UriComponents.HttpRequestUrl | UriComponents.UserInfo, UriFormat.UriEscaped);
            Download download;
            lock (Gate)
            {
                TrimCache();
                if (Cache.TryGetValue(key, out var cached))
                {
                    cached.References++;
                    cached.LastUsed = Environment.TickCount64;
                    return new(cached.Path, cached);
                }

                if (!Downloads.TryGetValue(key, out download!))
                {
                    download = new();
                    Downloads.Add(key, download);
                    download.Task = DownloadAsync(uri, download.Cancellation.Token);
                }

                download.Waiters++;
            }

            try
            {
                var entry = await download.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                lock (Gate)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    entry.References++;
                    if (!entry.CacheConsidered)
                    {
                        entry.CacheConsidered = true;
                        if (entry.Expires > DateTimeOffset.UtcNow && entry.Length <= MaximumCacheBytes)
                        {
                            entry.References++;
                            Cache.Add(key, entry);
                            _cacheBytes += entry.Length;
                            TrimCache();
                        }
                    }

                    return new(entry.Path, entry);
                }
            }
            finally
            {
                lock (Gate)
                    if (--download.Waiters == 0)
                    {
                        Downloads.Remove(key);
                        download.Cancellation.Cancel();
                        _ = download.Task.ContinueWith(completed =>
                        {
                            lock (Gate)
                                if (completed.IsCompletedSuccessfully) Release(completed.Result);
                                else _ = completed.Exception;

                            download.Cancellation.Dispose();
                        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                    }
            }
        }

        private static void TrimCache()
        {
            lock (Gate)
                while (Cache.Count > 0)
                {
                    string? victim = null;
                    var oldest = long.MaxValue;
                    foreach (var pair in Cache)
                    {
                        if (pair.Value.Expires <= DateTimeOffset.UtcNow)
                        {
                            victim = pair.Key;
                            break;
                        }

                        if ((Cache.Count > MaximumCacheEntries || _cacheBytes > MaximumCacheBytes)
                            && pair.Value.LastUsed < oldest)
                        {
                            victim = pair.Key;
                            oldest = pair.Value.LastUsed;
                        }
                    }

                    if (victim is null) break;
                    var entry = Cache[victim];
                    Cache.Remove(victim);
                    _cacheBytes -= entry.Length;
                    Release(entry);
                }
        }

        private static void ClearCache()
        {
            lock (Gate)
            {
                foreach (var entry in Cache.Values) Release(entry);
                Cache.Clear();
                _cacheBytes = 0;
            }

            GC.KeepAlive(ExpirationTimer);
        }

        private static void Release(Entry entry)
        {
            if (--entry.References == 0) DeleteTemporary(entry.Path);
        }
    }
}

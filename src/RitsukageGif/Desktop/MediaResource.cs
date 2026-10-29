using System.Buffers;
using System.Diagnostics;

namespace RitsukageGif.Desktop
{
    internal sealed partial class MediaResource : IDisposable
    {
        private static readonly HttpClient Client = new();
        private readonly Entry? _entry;
        private int _disposed;

        private MediaResource(string path, Entry? entry = null)
        {
            Path = path;
            _entry = entry;
        }

        public string Path { get; }
        private static TimeSpan BodyIdleTimeout { get; } = TimeSpan.FromSeconds(30);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0 || _entry is null) return;
            lock (Gate) Release(_entry);
        }

        public static async Task<MediaResource> OpenAsync(string location, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Uri.TryCreate(location, UriKind.Absolute, out var uri) || uri.IsFile)
            {
                var local = System.IO.Path.GetFullPath(uri?.IsFile == true ? uri.LocalPath : location);
                if (!File.Exists(local)) throw new FileNotFoundException(null, local);
                return new(local);
            }

            if (uri.Scheme is not ("http" or "https"))
                throw new NotSupportedException(UiText.Get("UnsupportedMediaLocation"));
            return await OpenRemoteAsync(uri, cancellationToken).ConfigureAwait(false);
        }

        private static async Task<Entry> DownloadAsync(Uri uri, CancellationToken cancellationToken)
        {
            using var response = await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                $"RitsukageGif-media-{Guid.NewGuid():N}.tmp");
            var owned = false;
            try
            {
                long length;
                await using (var input =
                             await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read,
                                 65536, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    owned = true;
                    using var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    var buffer = ArrayPool<byte>.Shared.Rent(65536);
                    try
                    {
                        while (true)
                        {
                            idle.CancelAfter(BodyIdleTimeout);
                            int count;
                            try
                            {
                                count = await input.ReadAsync(buffer.AsMemory(), idle.Token).ConfigureAwait(false);
                            }
                            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                            {
                                throw new TimeoutException(UiText.Get("MediaDownloadTimedOut"));
                            }

                            idle.CancelAfter(Timeout.InfiniteTimeSpan);
                            if (count == 0) break;
                            await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                        }

                        length = output.Length;
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                var expires = DateTimeOffset.MinValue;
                var cache = response.Headers.CacheControl;
                // Only explicitly fresh responses are retained; absent directives never imply freshness.
                if (cache is { NoStore: false, NoCache: false, MaxAge: { } maxAge } &&
                    response.Headers.Vary.Count == 0)
                {
                    var now = DateTimeOffset.UtcNow;
                    var age = response.Headers.Age ?? TimeSpan.Zero;
                    if (response.Headers.Date is { } date && now - date > age) age = now - date;
                    var remaining = maxAge - age;
                    if (remaining > TimeSpan.Zero)
                        expires = now + (remaining < TimeSpan.FromMinutes(5)
                            ? remaining
                            : TimeSpan.FromMinutes(5));
                }

                return new(path, length, expires);
            }
            catch
            {
                if (owned) DeleteTemporary(path);
                throw;
            }
        }

        private static void DeleteTemporary(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Trace.TraceWarning("Temporary media cleanup failed: {0}", error.Message);
            }
        }
    }
}

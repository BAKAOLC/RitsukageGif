using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ImageMagick;

namespace RitsukageGif.Desktop
{
    internal sealed class AnimationEditorThumbnailCache : IDisposable
    {
        private const int Capacity = 256;
        private const int PendingCapacity = 128;
        private readonly Dictionary<string, AnimationEditorThumbnailEntry> _entries = new(StringComparer.Ordinal);
        private readonly List<AnimationEditorThumbnailEntry> _pending = [];
        private readonly Lock _sync = new();
        private bool _disposed;
        private long _stamp;
        private int _workers;

        public void Dispose()
        {
            lock (_sync)
            {
                _disposed = true;
                Clear();
            }
        }

        public bool TryAcquire(string path, out AnimationEditorThumbnailLease? lease)
        {
            lock (_sync)
                if (!_disposed && _entries.TryGetValue(path, out var entry) && entry.Bitmap is { } bitmap)
                {
                    entry.Leases++;
                    entry.Stamp = ++_stamp;
                    lease = new(bitmap, () => Release(entry));
                    return true;
                }

            lease = null;
            return false;
        }

        public Task<AnimationEditorThumbnailLease?> AcquireAsync(string path, CancellationToken token = default)
        {
            return AcquireAsync(path, false, token);
        }

        private async Task<AnimationEditorThumbnailLease?> AcquireAsync(string path, bool prefetch,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            AnimationEditorThumbnailEntry entry;
            lock (_sync)
            {
                if (_disposed) return null;
                if (!_entries.TryGetValue(path, out entry!))
                {
                    if (_pending.Count == PendingCapacity)
                    {
                        var obsolete = _pending.Find(candidate => candidate.PrefetchOnly);
                        if (obsolete is null) return null;
                        RetirePending(obsolete);
                    }

                    entry = new() { Path = path, Leases = 1, PrefetchOnly = prefetch };
                    _entries.Add(path, entry);
                    _pending.Add(entry);
                    if (_workers < 2)
                    {
                        _workers++;
                        _ = Task.Run(DecodePending, CancellationToken.None);
                    }
                }
                else
                {
                    entry.Leases++;
                    if (!prefetch) entry.PrefetchOnly = false;
                }

                entry.Stamp = ++_stamp;
            }

            var released = 0;

            void ReleaseOnce()
            {
                lock (_sync)
                {
                    if (released != 0) return;
                    released = 1;
                    Release(entry);
                }
            }

            await using var registration = token.Register(ReleaseOnce);
            try
            {
                var bitmap = await entry.Loading.WaitAsync(token).ConfigureAwait(false);
                // Stop cancellation callbacks before handing ownership of the bitmap to the caller.
                // ReSharper disable once DisposeOnUsingVariable
                await registration.DisposeAsync().ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (bitmap is null)
                {
                    ReleaseOnce();
                    return null;
                }

                return new(bitmap, ReleaseOnce);
            }
            catch
            {
                ReleaseOnce();
                throw;
            }
        }

        public async Task PrefetchAsync(IEnumerable<string> paths, CancellationToken token = default)
        {
            var requests = paths.Distinct(StringComparer.Ordinal).Take(PendingCapacity).Select(async path =>
            {
                using var lease = await AcquireAsync(path, true, token).ConfigureAwait(false);
            });
            await Task.WhenAll(requests).ConfigureAwait(false);
        }

        private void RetirePending(AnimationEditorThumbnailEntry entry)
        {
            _pending.Remove(entry);
            if (_entries.TryGetValue(entry.Path, out var current) && ReferenceEquals(current, entry))
                _entries.Remove(entry.Path);
            entry.Retired = true;
            entry.Completion.TrySetResult(null);
        }

        private void DecodePending()
        {
            while (true)
            {
                AnimationEditorThumbnailEntry entry;
                lock (_sync)
                {
                    if (_pending.Count == 0)
                    {
                        _workers--;
                        return;
                    }

                    var index = _pending.FindIndex(candidate => !candidate.PrefetchOnly);
                    if (index < 0) index = 0;
                    entry = _pending[index];
                    _pending.RemoveAt(index);
                    if (entry.Leases == 0 || entry.Retired || _disposed)
                    {
                        RetirePending(entry);
                        continue;
                    }

                    entry.Started = true;
                }

                Bitmap? bitmap = null;
                try
                {
                    bitmap = Decode(entry.Path);
                }
                catch (Exception)
                {
                    /* A missing frame does not prevent other thumbnails from loading. */
                }

                lock (_sync)
                {
                    entry.Bitmap = bitmap;
                    if (bitmap is null)
                    {
                        if (_entries.TryGetValue(entry.Path, out var current) && ReferenceEquals(current, entry))
                            _entries.Remove(entry.Path);
                        entry.Retired = true;
                    }
                    else if (entry is { Retired: true, Leases: 0 })
                    {
                        entry.Bitmap = null;
                        bitmap.Dispose();
                        bitmap = null;
                    }

                    entry.Completion.TrySetResult(bitmap);
                    Evict();
                }
            }
        }

        private static Bitmap Decode(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            Span<byte> header = stackalloc byte[24];
            stream.ReadExactly(header);
            if (!header[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
                throw new InvalidDataException();
            var width = BinaryPrimitives.ReadInt32BigEndian(header[16..20]);
            var height = BinaryPrimitives.ReadInt32BigEndian(header[20..24]);
            if (width <= 0 || height <= 0) throw new InvalidDataException();
            stream.Position = 0;
            if (width / (double)height < 1d / 96 || height / (double)width < 1d / 152)
            {
                if ((long)width * height > 64 * 1024 * 1024) throw new InvalidDataException();
                using var image = new MagickImage(stream);
                var scale = Math.Min(1, Math.Min(152d / width, 96d / height));
                image.Resize(new MagickGeometry((uint)Math.Max(1, Math.Round(width * scale)),
                    (uint)Math.Max(1, Math.Round(height * scale))) { IgnoreAspectRatio = true });
                using var pixels = image.GetPixels();
                var bytes = pixels.ToByteArray(PixelMapping.BGRA)!;
                var bitmap = new WriteableBitmap(new((int)image.Width, (int)image.Height), new(96, 96),
                    PixelFormat.Bgra8888, AlphaFormat.Unpremul);
                using var buffer = bitmap.Lock();
                for (var y = 0; y < image.Height; y++)
                    Marshal.Copy(bytes, y * (int)image.Width * 4, buffer.Address + y * buffer.RowBytes,
                        (int)image.Width * 4);
                return bitmap;
            }

            return width / 152d >= height / 96d
                ? Bitmap.DecodeToWidth(stream, Math.Min(width, 152))
                : Bitmap.DecodeToHeight(stream, Math.Min(height, 96));
        }

        private void Release(AnimationEditorThumbnailEntry entry)
        {
            lock (_sync)
            {
                entry.Leases--;
                if (entry is { Leases: 0, Started: false, Loading.IsCompleted: false }) RetirePending(entry);
                if (entry is { Retired: true, Leases: 0 })
                {
                    entry.Bitmap?.Dispose();
                    entry.Bitmap = null;
                }

                Evict();
            }
        }

        private void Evict()
        {
            while (_entries.Count > Capacity)
            {
                AnimationEditorThumbnailEntry? oldest = null;
                foreach (var candidate in _entries.Values)
                    if (candidate is { Leases: 0, Bitmap: not null } &&
                        (oldest is null || candidate.Stamp < oldest.Stamp))
                        oldest = candidate;
                if (oldest is null) return;
                _entries.Remove(oldest.Path);
                oldest.Retired = true;
                oldest.Bitmap?.Dispose();
                oldest.Bitmap = null;
            }
        }

        public void Clear()
        {
            lock (_sync)
            {
                foreach (var entry in _entries.Values)
                {
                    entry.Retired = true;
                    if (entry.Leases == 0)
                    {
                        entry.Bitmap?.Dispose();
                        entry.Bitmap = null;
                    }
                }

                foreach (var entry in _pending) entry.Completion.TrySetResult(null);
                _pending.Clear();
                _entries.Clear();
            }
        }
    }
}

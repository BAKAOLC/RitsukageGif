using Avalonia.Threading;
using SkiaSharp;

namespace RitsukageGif.Desktop
{
    internal static partial class PinnedFileIconProvider
    {
        private static readonly ILogger Logger = LogHub.GetLogger("Desktop.Pins.FileIcons");
        private static readonly Lock Sync = new();
        private static readonly SemaphoreSlim Workers = new(2);

        private static readonly Dictionary<(string Path, int Size), (byte[]? Png, long Created)> Cache = [];
        private static long _cacheBytes;

        internal static async Task<byte[]?> ReadAsync(string path, int pixelSize, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var size = Math.Clamp(pixelSize, 16, 256);
            var key = (Path.GetFullPath(path), size);
            if (TryReadCache(key, out var cached)) return cached;
            await Workers.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (TryReadCache(key, out cached)) return cached;
                byte[]? png;
                try
                {
                    byte[] encoded;
                    if (OperatingSystem.IsLinux())
                        encoded = await ReadLinuxAsync(key.Item1, size, token).ConfigureAwait(false);
                    else if (OperatingSystem.IsMacOS())
                        encoded = await Dispatcher.UIThread.InvokeAsync(() => ReadPlatform(key.Item1, size, token),
                            DispatcherPriority.Background, token);
                    else
                        encoded = await Task.Run(() => ReadPlatform(key.Item1, size, token), token)
                            .ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    png = NormalizeIcon(encoded, size);
                }
                catch (Exception error) when (error is not OperationCanceledException and not OutOfMemoryException)
                {
                    Logger.ZLogWarning(
                        $"System file icon lookup failed. ErrorType={error.GetType().Name} Code={error.HResult}");
                    png = null;
                }

                token.ThrowIfCancellationRequested();
                lock (Sync)
                {
                    if (Cache.Remove(key, out var previous)) _cacheBytes -= previous.Png?.Length ?? 0;
                    while (Cache.Count >= 128 || _cacheBytes + (png?.Length ?? 0) > 4L * 1024 * 1024)
                    {
                        var oldest = Cache.MinBy(entry => entry.Value.Created);
                        Cache.Remove(oldest.Key);
                        _cacheBytes -= oldest.Value.Png?.Length ?? 0;
                    }

                    Cache[key] = (png, Environment.TickCount64);
                    _cacheBytes += png?.Length ?? 0;
                }

                return png;
            }
            finally
            {
                Workers.Release();
            }
        }

        private static byte[] ReadPlatform(string path, int size, CancellationToken token)
        {
            if (OperatingSystem.IsWindows()) return ReadWindows(path, size, token);
            if (OperatingSystem.IsMacOS()) return ReadMac(path, size, token);
            throw new PlatformNotSupportedException("System file icons are unavailable on this platform.");
        }

        private static bool TryReadCache((string Path, int Size) key, out byte[]? png)
        {
            lock (Sync)
                if (Cache.TryGetValue(key, out var entry))
                {
                    if (Environment.TickCount64 - entry.Created < 120_000)
                    {
                        png = entry.Png;
                        return true;
                    }

                    Cache.Remove(key);
                    _cacheBytes -= entry.Png?.Length ?? 0;
                }

            png = null;
            return false;
        }

        private static byte[] NormalizeIcon(byte[] png, int size)
        {
            using var source = SKImage.FromEncodedData(png) ??
                               throw new InvalidDataException("The system file icon could not be decoded.");
            if (source.Width == size && source.Height == size) return png;
            using var surface = SKSurface.Create(new SKImageInfo(size, size));
            surface.Canvas.Clear(SKColors.Transparent);
            var scale = Math.Min((float)size / source.Width, (float)size / source.Height);
            var width = source.Width * scale;
            var height = source.Height * scale;
            surface.Canvas.DrawImage(source, new SKRect((size - width) / 2, (size - height) / 2,
                    (size + width) / 2, (size + height) / 2),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
            using var image = surface.Snapshot();
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            return encoded.ToArray();
        }
    }
}

using System.Globalization;

namespace RitsukageGif.Desktop
{
    internal sealed class ImageEditorHistoryStore : IDisposable
    {
        private static readonly ILogger Logger = LogHub.GetLogger("Desktop.ImageEditorHistory");
        private readonly OwnedWorkerDirectory _directory;
        private bool _disposed;
        private long _nextId;

        internal ImageEditorHistoryStore(string? rootDirectory = null)
        {
            _directory = OwnedWorkerDirectory.Create("ImageEditorHistory", rootDirectory);
        }

        private string DirectoryPath => _directory.DirectoryPath;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _directory.Dispose();
        }

        internal async Task<ImageEditorStoredDocument> WriteAsync(byte[] png, CapturedFrame? hdr,
            CancellationToken token = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            token.ThrowIfCancellationRequested();
            var stem = (++_nextId).ToString("x", CultureInfo.InvariantCulture);
            var pngPath = Path.Combine(DirectoryPath, stem + ".png");
            var hdrPath = hdr is null ? null : Path.Combine(DirectoryPath, stem + ".pixels");
            try
            {
                await File.WriteAllBytesAsync(pngPath, png, token).ConfigureAwait(false);
                if (hdrPath is not null)
                {
                    await using var stream = new FileStream(hdrPath, FileMode.CreateNew, FileAccess.Write,
                        FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    await stream.WriteAsync(hdr!.Pixels, token).ConfigureAwait(false);
                }

                token.ThrowIfCancellationRequested();
                return new(pngPath, hdrPath, hdr is null ? null : hdr with { Pixels = ReadOnlyMemory<byte>.Empty },
                    (long)png.Length + (hdr?.Pixels.Length ?? 0));
            }
            catch
            {
                DeleteFile(pngPath);
                if (hdrPath is not null) DeleteFile(hdrPath);
                throw;
            }
        }

        internal async Task<ImageStripResult> ReadAsync(ImageEditorStoredDocument document,
            CancellationToken token = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var png = await File.ReadAllBytesAsync(document.PngPath, token).ConfigureAwait(false);
            CapturedFrame? hdr = null;
            if (document.HdrPath is not null)
            {
                var pixels = await File.ReadAllBytesAsync(document.HdrPath, token).ConfigureAwait(false);
                hdr = document.HdrMetadata! with { Pixels = pixels };
            }

            token.ThrowIfCancellationRequested();
            return new(png, hdr);
        }

        internal void Delete(ImageEditorStoredDocument document)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            DeleteFile(document.PngPath);
            if (document.HdrPath is not null) DeleteFile(document.HdrPath);
        }

        private static void DeleteFile(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Logger.ZLogWarning(error, $"Image editor temporary data cleanup was deferred.");
            }
        }
    }
}

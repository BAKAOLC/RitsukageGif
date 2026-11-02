using System.Text;

namespace RitsukageGif.Core
{
    public static class AtomicFileOutput
    {
        private static readonly ILogger Logger = LogHub.GetLogger("Core.Output");
        private static readonly UTF8Encoding Utf8 = new(false);

        public static Task WriteBytesAsync(string path, ReadOnlyMemory<byte> bytes, CancellationToken token = default)
        {
            return WriteAsync(path, (stream, cancellation) => stream.WriteAsync(bytes, cancellation).AsTask(), token);
        }

        public static Task WriteTextAsync(string path, string text, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (text.Length <= 4096) return WriteBytesAsync(path, Utf8.GetBytes(text), token);
            return WriteAsync(path, async (stream, cancellation) =>
            {
                await using var writer = new StreamWriter(stream, Utf8, 16384, true);
                await writer.WriteAsync(text.AsMemory(), cancellation).ConfigureAwait(false);
                await writer.FlushAsync(cancellation).ConfigureAwait(false);
            }, token);
        }

        private static async Task WriteAsync(string path, Func<Stream, CancellationToken, Task> write,
            CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            path = Path.GetFullPath(path);
            var temporary = Path.Combine(Path.GetDirectoryName(path)!, "." + Path.GetFileName(path) + "." +
                                                                       Guid.NewGuid().ToString("N") + ".stage");
            try
            {
                await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                                 FileShare.None, 1, FileOptions.Asynchronous | FileOptions.SequentialScan))
                    await write(stream, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                File.Move(temporary, path, true);
            }
            finally
            {
                try
                {
                    File.Delete(temporary);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    Logger.ZLogWarning(error, $"Output staging cleanup was deferred.");
                }
            }
        }
    }
}

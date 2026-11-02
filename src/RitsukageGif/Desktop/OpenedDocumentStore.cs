using System.Diagnostics;

namespace RitsukageGif.Desktop
{
    internal sealed class OpenedDocumentStore
    {
        private static readonly string[] StagePrefixes = [".Document.docx.", ".Document.xlsx."];
        private readonly string _root;
        private readonly Func<string, ReadOnlyMemory<byte>, CancellationToken, Task> _write;

        internal OpenedDocumentStore(Func<string, ReadOnlyMemory<byte>, CancellationToken, Task> write,
            string? directory = null)
        {
            _write = write;
            _root = Path.GetFullPath(directory ?? Path.Combine(Path.GetTempPath(), "RitsukageGif", "OpenedDocuments"));
        }

        internal async Task<OpenedDocumentLease> CreateAsync(ReadOnlyMemory<byte> bytes, string extension,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (extension is not (".docx" or ".xlsx")) throw new ArgumentOutOfRangeException(nameof(extension));
            Directory.CreateDirectory(_root);
            Cleanup();
            var directory = Path.Combine(_root, Guid.NewGuid().ToString("N"));
            if (OperatingSystem.IsWindows()) Directory.CreateDirectory(directory);
            else
                Directory.CreateDirectory(directory,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var lease = new OpenedDocumentLease(Path.Combine(directory, "Document" + extension));
            try
            {
                await _write(lease.Path, bytes, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                return lease;
            }
            catch
            {
                lease.Dispose();
                throw;
            }
        }

        private static bool OwnFile(string name)
        {
            if (name is "Document.docx" or "Document.xlsx") return true;
            foreach (var prefix in StagePrefixes)
                if (name.StartsWith(prefix, StringComparison.Ordinal) &&
                    name.EndsWith(".stage", StringComparison.Ordinal) &&
                    name.Length == prefix.Length + 38 && Guid.TryParseExact(name.AsSpan(prefix.Length, 32), "N", out _))
                    return true;
            return false;
        }

        private void Cleanup()
        {
            var threshold = DateTime.UtcNow.AddDays(-7);
            foreach (var directory in Directory.EnumerateDirectories(_root))
            {
                if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out _)) continue;
                try
                {
                    var files = Directory.GetFiles(directory);
                    if (Directory.EnumerateDirectories(directory).Any()) continue;
                    if (files.Length == 0)
                    {
                        if (Directory.GetLastWriteTimeUtc(directory) < threshold) Directory.Delete(directory);
                        continue;
                    }

                    if (files.Length != 1) continue;
                    var file = files[0];
                    if (!OwnFile(Path.GetFileName(file)) || File.GetLastWriteTimeUtc(file) >= threshold) continue;
                    using (File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                    }

                    File.Delete(file);
                    Directory.Delete(directory);
                    Trace.TraceInformation("An expired opened document was reclaimed.");
                }
                catch (DirectoryNotFoundException)
                {
                }
                catch (FileNotFoundException)
                {
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    Trace.TraceWarning("Opened document cleanup was deferred. ErrorType={0} Code={1}",
                        error.GetType().Name, error.HResult);
                }
            }
        }
    }
}

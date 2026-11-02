using System.Text;

namespace RitsukageGif.Desktop
{
    internal sealed class PinnedDocumentClipboardFiles
    {
        private static readonly ILogger Logger = LogHub.GetLogger("Desktop.Pins.ClipboardFiles");
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "RitsukageGif", "PinnedDocuments");

        public async Task<string> CreateAsync(string content, string extension, CancellationToken token)
        {
            Directory.CreateDirectory(_directory);
            var path = Path.Combine(_directory, "Document-" + Guid.NewGuid().ToString("N") + extension);
            try
            {
                await File.WriteAllTextAsync(path, content, new UTF8Encoding(false), token);
                return path;
            }
            catch
            {
                File.Delete(path);
                throw;
            }
        }

        public Task CleanupAsync(IReadOnlySet<string> protectedPaths)
        {
            return Task.Run(() =>
            {
                if (!Directory.Exists(_directory)) return;
                foreach (var path in Directory.EnumerateFiles(_directory, "Document-*", SearchOption.TopDirectoryOnly))
                {
                    if (protectedPaths.Contains(path) || !Owns(path)) continue;
                    try
                    {
                        if (File.GetLastWriteTimeUtc(path) < DateTime.UtcNow.AddDays(-7)) File.Delete(path);
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                    {
                        Logger.ZLogWarning(
                            $"Pinned clipboard document cleanup failed. ErrorType={error.GetType().Name} Code={error.HResult}");
                    }
                }
            });
        }

        public void Delete(string path)
        {
            if (!Owns(path)) throw new ArgumentOutOfRangeException(nameof(path));
            File.Delete(path);
        }

        private bool Owns(string path)
        {
            var resolved = Path.GetFullPath(path);
            var stem = Path.GetFileNameWithoutExtension(resolved);
            return string.Equals(Path.GetDirectoryName(resolved), _directory,
                       OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) &&
                   stem.StartsWith("Document-", StringComparison.Ordinal) &&
                   Guid.TryParseExact(stem.AsSpan(9), "N", out _) &&
                   Path.GetExtension(resolved) is ".txt" or ".html" or ".tex";
        }
    }
}

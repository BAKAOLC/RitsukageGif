using System.Diagnostics;

namespace RitsukageGif.Desktop
{
    internal sealed class ScreenshotClipboardFileStore(
        string? directory = null,
        ScreenshotClipboardFileNaming? naming = null)
    {
        private readonly string _directory = Path.GetFullPath(directory ??
                                                              Path.Combine(Path.GetTempPath(), "RitsukageGif",
                                                                  "Clipboard"));

        public async Task<string> CreateAsync(byte[] png, ScreenshotClipboardFileFormat format, int quality,
            CancellationToken token, string suffix = "")
        {
            ArgumentNullException.ThrowIfNull(png);
            if (png.Length == 0 || png.Length > PinnedImageContent.MaximumEncodedBytes ||
                !Enum.IsDefined(format) || quality is < 0 or > 100)
                throw new ArgumentOutOfRangeException(nameof(png));
            token.ThrowIfCancellationRequested();
            var extension = format switch
            {
                ScreenshotClipboardFileFormat.Png => ".png",
                ScreenshotClipboardFileFormat.Jpeg => ".jpg",
                ScreenshotClipboardFileFormat.Bmp => ".bmp",
                ScreenshotClipboardFileFormat.Webp => ".webp",
                ScreenshotClipboardFileFormat.Avif => ".avif",
                ScreenshotClipboardFileFormat.Disabled or ScreenshotClipboardFileFormat.Auto =>
                    throw new ArgumentOutOfRangeException(nameof(format)),
                _ => throw new ArgumentOutOfRangeException(nameof(format)),
            };
            var identifier = "Screenshot-" + Guid.NewGuid().ToString("N");
            var path = naming is null
                ? Path.Combine(_directory, identifier + extension)
                : Path.Combine(_directory, identifier, naming.CreateName(extension, suffix));
            try
            {
                var info = await ScreenshotOutputStorage.SaveAsync(png, path, new()
                {
                    Quality = quality, ConflictPolicy = ScreenshotConflictPolicy.Fail,
                }, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                return info.Path;
            }
            catch
            {
                Delete(path);
                throw;
            }
        }

        public Task CleanupAsync(IReadOnlySet<string> protectedPaths)
        {
            return Task.Run(() =>
            {
                if (!Directory.Exists(_directory)) return;
                foreach (var path in
                         Directory.EnumerateFiles(_directory, "Screenshot-*", SearchOption.TopDirectoryOnly))
                    CleanupFile(path, protectedPaths);
                foreach (var directory in Directory.EnumerateDirectories(_directory, "Screenshot-*"))
                {
                    if (!IsIdentifier(Path.GetFileName(directory)) ||
                        (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
                    foreach (var path in Directory.EnumerateFiles(directory)) CleanupFile(path, protectedPaths);
                }
            });
        }

        public void Delete(string path)
        {
            if (!Owns(path)) throw new ArgumentOutOfRangeException(nameof(path));
            try
            {
                File.Delete(path);
                var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
                if (!SamePath(directory, _directory) && Directory.Exists(directory) &&
                    !Directory.EnumerateFileSystemEntries(directory).Any())
                    Directory.Delete(directory);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Trace.TraceWarning("Could not remove clipboard image: {0}", error.Message);
            }
        }

        private bool Owns(string path)
        {
            var resolved = Path.GetFullPath(path);
            if (Path.GetExtension(resolved) is not (".png" or ".jpg" or ".bmp" or ".webp" or ".avif")) return false;
            var directory = Path.GetDirectoryName(resolved)!;
            if (SamePath(directory, _directory)) return IsIdentifier(Path.GetFileNameWithoutExtension(resolved));
            return SamePath(Path.GetDirectoryName(directory), _directory) &&
                   IsIdentifier(Path.GetFileName(directory)) &&
                   (!Directory.Exists(directory) || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0);
        }

        private void CleanupFile(string path, IReadOnlySet<string> protectedPaths)
        {
            if (!Owns(path) || protectedPaths.Contains(path)) return;
            try
            {
                if (File.GetLastWriteTimeUtc(path) < DateTime.UtcNow.AddDays(-7)) Delete(path);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Trace.TraceWarning("Could not inspect clipboard image: {0}", error.Message);
            }
        }

        private static bool SamePath(string? left, string right)
        {
            return string.Equals(left, right,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }

        private static bool IsIdentifier(string stem)
        {
            return stem.StartsWith("Screenshot-", StringComparison.Ordinal) &&
                   Guid.TryParseExact(stem[11..], "N", out _);
        }
    }
}

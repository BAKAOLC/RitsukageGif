using System.Diagnostics;

namespace RitsukageGif.Desktop
{
    internal sealed class ScreenshotClipboardFileStore(string? directory = null)
    {
        private readonly string _directory = Path.GetFullPath(directory ??
                                                              Path.Combine(Path.GetTempPath(), "RitsukageGif",
                                                                  "Clipboard"));

        public async Task<string> CreateAsync(byte[] png, ScreenshotClipboardFileFormat format, int quality,
            CancellationToken token)
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
                _ => throw new ArgumentOutOfRangeException(nameof(format)),
            };
            var path = Path.Combine(_directory, "Screenshot-" + Guid.NewGuid().ToString("N") + extension);
            var info = await ScreenshotOutputStorage.SaveAsync(png, path, new()
            {
                Quality = quality, ConflictPolicy = ScreenshotConflictPolicy.Fail,
            }, token).ConfigureAwait(false);
            try
            {
                token.ThrowIfCancellationRequested();
                return info.Path;
            }
            catch
            {
                Delete(info.Path);
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
                {
                    if (!Owns(path) || protectedPaths.Contains(path)) continue;
                    try
                    {
                        if (File.GetLastWriteTimeUtc(path) < DateTime.UtcNow.AddDays(-7)) Delete(path);
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                    {
                        Trace.TraceWarning("Could not inspect clipboard image: {0}", error.Message);
                    }
                }
            });
        }

        public void Delete(string path)
        {
            if (!Owns(path)) throw new ArgumentOutOfRangeException(nameof(path));
            try
            {
                File.Delete(path);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Trace.TraceWarning("Could not remove clipboard image: {0}", error.Message);
            }
        }

        private bool Owns(string path)
        {
            var resolved = Path.GetFullPath(path);
            if (!string.Equals(Path.GetDirectoryName(resolved), _directory,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                return false;
            var stem = Path.GetFileNameWithoutExtension(resolved);
            return stem.StartsWith("Screenshot-", StringComparison.Ordinal) &&
                   Guid.TryParseExact(stem[11..], "N", out _) &&
                   Path.GetExtension(resolved) is ".png" or ".jpg" or ".bmp" or ".webp";
        }
    }
}

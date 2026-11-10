using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;

namespace RitsukageGif.Desktop
{
    internal sealed class RecordingClipboardFileStore(string? root = null)
    {
        private static readonly SemaphoreSlim Gate = new(1);
        private static readonly ILogger Logger = LogHub.GetLogger("Desktop.RecordingClipboard");

        private readonly string _root =
            Path.GetFullPath(root ?? Path.Combine(Path.GetTempPath(), "RitsukageGif", "RecordingClipboard"));

        internal async Task<bool> PublishAsync(IClipboard clipboard, IStorageProvider storage, string name,
            Func<string, CancellationToken, Task<IReadOnlyList<string>>> encode, CancellationToken token)
        {
            await Gate.WaitAsync(token);
            try
            {
                await CleanupAsync(clipboard);
                var directory = Path.Combine(_root, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);
                var publicationAttempted = false;
                IReadOnlyList<string> outputs = [];
                try
                {
                    outputs = await encode(Path.Combine(directory, Path.GetFileName(name)), token);
                    if (outputs.Count == 0)
                    {
                        Directory.Delete(directory, true);
                        return false;
                    }

                    var data = new DataTransfer();
                    foreach (var path in outputs)
                    {
                        var file = await StoragePickerAwaiter.WaitAsync(storage.TryGetFileFromPathAsync(path), token) ??
                                   throw new IOException(UiText.Get("ClipboardFailed"));
                        data.Add(DataTransferItem.CreateFile(file));
                    }

                    token.ThrowIfCancellationRequested();
                    publicationAttempted = true;
                    await clipboard.SetDataAsync(data);
                    await clipboard.FlushAsync();
                    return true;
                }
                catch
                {
                    if (!publicationAttempted || await ReferencesAnyAsync(clipboard, outputs) == false)
                        Directory.Delete(directory, true);
                    throw;
                }
            }
            finally
            {
                Gate.Release();
            }
        }

        private async Task CleanupAsync(IClipboard clipboard)
        {
            try
            {
                var current = await clipboard.TryGetFilesAsync();
                var paths = current?.Select(file => file.TryGetLocalPath()).Where(path => path is not null).ToArray() ??
                            [];
                await Task.Run(() =>
                {
                    if (!Directory.Exists(_root)) return;
                    foreach (var directory in Directory.EnumerateDirectories(_root))
                    {
                        if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out _) ||
                            File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint) ||
                            Directory.GetLastWriteTimeUtc(directory) >= DateTime.UtcNow.AddDays(-7)) continue;
                        var prefix = directory + Path.DirectorySeparatorChar;
                        if (paths.Any(path => path!.StartsWith(prefix, OperatingSystem.IsWindows()
                                ? StringComparison.OrdinalIgnoreCase
                                : StringComparison.Ordinal))) continue;
                        Directory.Delete(directory, true);
                    }
                });
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                Logger.ZLogWarning(error, $"Recording clipboard cleanup was skipped.");
            }
        }

        private static async Task<bool?> ReferencesAnyAsync(IClipboard clipboard, IReadOnlyList<string> paths)
        {
            try
            {
                var files = await clipboard.TryGetFilesAsync();
                return files?.Any(file => paths.Contains(file.TryGetLocalPath(), OperatingSystem.IsWindows()
                    ? StringComparer.OrdinalIgnoreCase
                    : StringComparer.Ordinal)) == true;
            }
            catch
            {
                return null;
            }
        }
    }
}

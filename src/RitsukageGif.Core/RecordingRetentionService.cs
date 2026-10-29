using System.Text.Json;

namespace RitsukageGif.Core
{
    public sealed class RecordingRetentionService(string indexPath)
    {
        private static readonly ILogger Logger = LogHub.GetLogger("Core.Storage");
        private readonly SemaphoreSlim _gate = new(1, 1);

        private static StringComparer PathComparer => OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        public async Task ApplyAsync(RecordingRetentionPolicy policy, IReadOnlyList<string> newFiles,
            IReadOnlyList<string> protectedPaths, IReadOnlyList<string>? clipboardPaths = null,
            CancellationToken token = default)
        {
            await _gate.WaitAsync(token).ConfigureAwait(false);
            Logger.ZLogDebug(
                $"Recording retention pass started: rulesEnabled={policy.HasRules}, newFiles={newFiles.Count}, protectedFiles={protectedPaths.Count}");
            try
            {
                await Task.Run(() => Apply(policy, newFiles, protectedPaths, clipboardPaths, token), token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                Logger.ZLogDebug($"Recording retention pass canceled");
                throw;
            }
            catch (Exception error)
            {
                Logger.ZLogError(error,
                    $"Recording retention pass failed: exceptionType={error.GetType().Name}, hResult={error.HResult}");
                throw;
            }
            finally
            {
                _gate.Release();
            }
        }

        private void Apply(RecordingRetentionPolicy policy, IReadOnlyList<string> newFiles,
            IReadOnlyList<string> protectedPaths, IReadOnlyList<string>? clipboardPaths, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var index = File.Exists(indexPath)
                ? JsonSerializer.Deserialize(File.ReadAllText(indexPath),
                    RecordingRetentionJsonContext.Default.RecordingRetentionIndex) ?? throw new InvalidDataException()
                : new();
            if (index.Files is null || index.ClipboardPaths is null) throw new InvalidDataException();
            // JSON can contain null collection elements despite the C# annotation.
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (index.Files.Any(entry => entry is null || string.IsNullOrWhiteSpace(entry.Path) || entry.Length < 0))
                throw new InvalidDataException();
            var now = DateTime.UtcNow;
            foreach (var path in newFiles)
            {
                token.ThrowIfCancellationRequested();
                var fullPath = Path.GetFullPath(path);
                if (!File.Exists(fullPath) || !SafeFile(fullPath)) continue;
                var file = new FileInfo(fullPath);
                if (!file.Exists) continue;
                index.Files.RemoveAll(entry => PathComparer.Equals(entry.Path, fullPath));
                index.Files.Add(new(fullPath, now, file.Length, file.LastWriteTimeUtc,
                    file.CreationTimeUtc));
            }

            if (clipboardPaths is not null) index.ClipboardPaths = [.. clipboardPaths.Select(Path.GetFullPath)];
            var protect = new HashSet<string>(protectedPaths.Select(Path.GetFullPath), PathComparer);
            protect.UnionWith(index.ClipboardPaths);
            // Never adopt arbitrary files from the output directory. Changed or replaced files stop being managed.
            var files = new List<RetainedRecording>();
            var inaccessible = 0;
            foreach (var entry in index.Files.DistinctBy(entry => entry.Path, PathComparer))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if (Matches(entry)) files.Add(entry);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    files.Add(entry); // Retry inaccessible files later, without treating them as deletion candidates.
                    inaccessible++;
                }
            }

            index.Files.Clear();
            index.Files.AddRange(files.OrderBy(entry => entry.SavedUtc).ThenBy(entry => entry.Path, PathComparer));
            Save(index); // Persist ownership and clipboard protection before removing any file.
            if (!policy.HasRules)
            {
                Logger.ZLogDebug(
                    $"Recording retention ownership updated without deletion rules: trackedFiles={index.Files.Count}, inaccessibleFiles={inaccessible}");
                return;
            }

            var count = index.Files.Count;
            var originalCount = count;
            var deferred = 0;
            Exception? deferredError = null;
            string? deferredPath = null;
            var total = index.Files.Sum(entry => (decimal)entry.Length);
            var maxBytes = policy.MaxSizeMiB is > 0 ? (decimal)policy.MaxSizeMiB.Value * 1024 * 1024 : decimal.MaxValue;
            var cutoff = policy.MaxAgeDays is > 0
                ? now.AddDays(-Math.Min(policy.MaxAgeDays.Value, 365000))
                : DateTime.MinValue;
            foreach (var entry in index.Files.ToArray())
            {
                token.ThrowIfCancellationRequested();
                if (protect.Contains(entry.Path)) continue;
                if (entry.SavedUtc >= cutoff && !(policy.MaxFiles is > 0 && count > policy.MaxFiles) &&
                    total <= maxBytes)
                    continue;
                try
                {
                    if (!Matches(entry)) continue;
                    File.Delete(entry.Path);
                    index.Files.Remove(entry);
                    count--;
                    total -= entry.Length;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    deferred++;
                    if (deferredError is null)
                    {
                        deferredError = error;
                        deferredPath = entry.Path;
                    }
                }
            }

            Save(index);
            if (deferredError is not null)
                Logger.ZLogWarning(deferredError,
                    $"Recording file deletion deferred: files={deferred}, firstPath={deferredPath}");
            Logger.ZLogInformation(
                $"Recording retention pass completed: deletedFiles={originalCount - count}, remainingFiles={count}, deferredFiles={deferred}, inaccessibleFiles={inaccessible}");
        }

        private void Save(RecordingRetentionIndex index)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(indexPath))!);
            var temporary = indexPath + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonSerializer.Serialize(index,
                    RecordingRetentionJsonContext.Default.RecordingRetentionIndex));
                File.Move(temporary, indexPath, true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private static bool Matches(RetainedRecording entry)
        {
            if (!Path.IsPathFullyQualified(entry.Path) || !File.Exists(entry.Path) || !SafeFile(entry.Path))
                return false;
            var file = new FileInfo(entry.Path);
            return file.Exists && file.Length == entry.Length && file.LastWriteTimeUtc == entry.LastWriteUtc &&
                   file.CreationTimeUtc == entry.CreatedUtc;
        }

        private static bool SafeFile(string path)
        {
            if (Path.GetExtension(path).ToLowerInvariant() is not (".gif" or ".webp" or ".png" or ".jpg" or ".jpeg"
                or ".bmp" or ".exr" or ".mp4" or ".webm"))
                return false;
            for (var current = path; current is not null; current = Path.GetDirectoryName(current))
            {
                // Cloud placeholders are reparse points too; reject links and junctions, not cloud storage.
                FileSystemInfo item = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
                if (item.LinkTarget is not null) return false;
            }

            return true;
        }
    }
}

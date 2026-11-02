using System.Text.Json;

namespace RitsukageGif.Desktop
{
    internal sealed partial class CaptureHistoryPersistence : IAsyncDisposable
    {
        private static readonly ILogger Logger = LogHub.GetLogger("Desktop.Capture.History");
        private readonly string _directory;
        private readonly SettingsPersistenceQueue<CaptureHistorySnapshot> _queue;
        private long _clearedForensicsRevision;
        private bool _quarantinePending;

        internal CaptureHistoryPersistence(string directory)
        {
            _directory = Path.GetFullPath(directory);
            _queue = new(WriteAsync,
                error => Logger.ZLogWarning(error, $"Capture history persistence failed."),
                TimeSpan.FromMilliseconds(300));
        }

        internal bool DeferInitialWrite { get; private set; }

        private string ManifestPath => Path.Combine(_directory, "manifest.json");

        public ValueTask DisposeAsync()
        {
            return _queue.DisposeAsync();
        }

        internal void Request(CaptureHistorySnapshot snapshot)
        {
            _queue.RequestSave(snapshot);
        }

        internal Task FlushAsync(CancellationToken token)
        {
            return _queue.FlushAsync(token);
        }

        private string ImagePath(Guid id, bool hdr)
        {
            return Path.Combine(_directory, id.ToString("N") + (hdr ? ".hdr" : ".png"));
        }

        private async Task WriteAsync(CaptureHistorySnapshot snapshot, CancellationToken token)
        {
            if (!snapshot.Enabled)
            {
                DeleteUnusedFiles([]);
                DeferInitialWrite = false;
                _quarantinePending = false;
                Logger.ZLogInformation($"Capture history cache cleared while persistence is disabled.");
                return;
            }

            if (_quarantinePending) QuarantineManifest();
            var manifest = CreateManifest(snapshot);
            if (!TrimManifest(manifest, snapshot.MaximumBytes))
            {
                DeleteUnusedFiles([]);
                Logger.ZLogInformation(
                    $"Capture history cache cleared because the configured byte budget cannot hold its metadata.");
                return;
            }

            Directory.CreateDirectory(_directory);
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var created = new List<string>();
            var committed = false;
            try
            {
                var retained = manifest.Images.Select(entry => entry.Id).ToHashSet();
                foreach (var entry in snapshot.Images)
                {
                    if (!retained.Contains(entry.Id)) continue;
                    var pngPath = ImagePath(entry.Id, false);
                    await WritePayloadAsync(pngPath, entry.Png, created, token).ConfigureAwait(false);
                    keep.Add(Path.GetFileName(pngPath));
                    if (entry.Hdr is { } hdr)
                    {
                        var hdrPath = ImagePath(entry.Id, true);
                        await WritePayloadAsync(hdrPath, hdr.Pixels, created, token).ConfigureAwait(false);
                        keep.Add(Path.GetFileName(hdrPath));
                    }
                }

                await WriteManifestAsync(manifest, token).ConfigureAwait(false);
                committed = true;
                DeferInitialWrite = false;
                keep.Add("manifest.json");
                if (snapshot.ClearRevision == _clearedForensicsRevision) keep.Add("manifest.rejected.json");
                DeleteUnusedFiles(keep);
                _clearedForensicsRevision = snapshot.ClearRevision;
                Logger.ZLogInformation(
                    $"Capture history persisted. RegionCount={manifest.Regions.Count} ImageCount={manifest.Images.Count}");
            }
            finally
            {
                if (!committed)
                    foreach (var path in created)
                        try
                        {
                            File.Delete(path);
                        }
                        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                        {
                            Logger.ZLogWarning(error, $"Capture history transaction rollback failed.");
                        }
            }
        }

        private static CaptureHistoryManifest CreateManifest(CaptureHistorySnapshot snapshot)
        {
            return new()
            {
                Regions =
                [
                    .. snapshot.Regions.Select(entry => new CaptureHistoryRegionData
                    {
                        Id = entry.Id, Timestamp = entry.Timestamp, Region = entry.Selection.Region,
                        WindowSource = entry.Selection.WindowSource, Cursor = entry.Selection.Cursor,
                        Shape = entry.Selection.Shape,
                        Target = entry.Selection.Target is { } target
                            ? CaptureHistoryWindowData.FromWindow(target)
                            : null,
                        Windows = entry.Selection.Windows?.Select(CaptureHistoryWindowData.FromWindow).ToList(),
                    }),
                ],
                Images =
                [
                    .. snapshot.Images.Select(entry => new CaptureHistoryImageData
                    {
                        Id = entry.Id, Timestamp = entry.Timestamp, Region = entry.Region, Source = entry.Source,
                        PngBytes = entry.Png.Length, CaptureScaling = entry.CaptureScaling,
                        Decoration = entry.Decoration, OutputOrigin = entry.OutputOrigin,
                        Hdr = entry.Hdr is { } hdr
                            ? new CaptureHistoryHdrData
                            {
                                Width = hdr.Width, Height = hdr.Height, Stride = hdr.Stride,
                                PixelFormat = hdr.PixelFormat,
                                AlphaMode = hdr.AlphaMode, Revision = hdr.Revision, PixelBytes = hdr.Pixels.Length,
                                ColorInfo = hdr.ColorInfo, ColorRegions = [.. hdr.ColorRegions],
                            }
                            : null,
                    }),
                ],
            };
        }

        private static bool TrimManifest(CaptureHistoryManifest manifest, long budget)
        {
            using var counter = new CaptureHistoryCountingStream();
            JsonSerializer.Serialize(counter, new(), CaptureHistoryJsonContext.Default.CaptureHistoryManifest);
            var total = counter.Count;
            if (total > budget) return false;
            var regionCosts = new Dictionary<Guid, long>();
            var imageCosts = new Dictionary<Guid, long>();
            foreach (var entry in manifest.Regions)
            {
                counter.Reset();
                JsonSerializer.Serialize(counter, entry, CaptureHistoryJsonContext.Default.CaptureHistoryRegionData);
                regionCosts.Add(entry.Id, counter.Count);
                total += counter.Count;
            }

            foreach (var entry in manifest.Images)
            {
                counter.Reset();
                JsonSerializer.Serialize(counter, entry, CaptureHistoryJsonContext.Default.CaptureHistoryImageData);
                var cost = counter.Count + entry.PngBytes + (entry.Hdr?.PixelBytes ?? 0);
                imageCosts.Add(entry.Id, cost);
                total += cost;
            }

            total += Math.Max(0, manifest.Regions.Count - 1) + Math.Max(0, manifest.Images.Count - 1);
            while (total > budget)
                if (manifest.Regions.Count > 0 && (manifest.Images.Count == 0 ||
                                                   manifest.Regions[^1].Timestamp <= manifest.Images[^1].Timestamp))
                {
                    var count = manifest.Regions.Count;
                    total -= regionCosts[manifest.Regions[^1].Id] + (count > 1 ? 1 : 0);
                    manifest.Regions.RemoveAt(count - 1);
                }
                else
                {
                    var count = manifest.Images.Count;
                    total -= imageCosts[manifest.Images[^1].Id] + (count > 1 ? 1 : 0);
                    manifest.Images.RemoveAt(count - 1);
                }

            return true;
        }

        private async Task WriteManifestAsync(CaptureHistoryManifest manifest, CancellationToken token)
        {
            var temporary = ManifestPath + ".pending";
            try
            {
                await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None,
                                 65536, true))
                    await JsonSerializer
                        .SerializeAsync(stream, manifest, CaptureHistoryJsonContext.Default.CaptureHistoryManifest,
                            token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                File.Move(temporary, ManifestPath, true);
            }
            finally
            {
                try
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    Logger.ZLogWarning(error, $"Capture history temporary manifest cleanup failed.");
                }
            }
        }

        private void QuarantineManifest()
        {
            if (File.Exists(ManifestPath))
            {
                var forensic = Path.Combine(_directory, "manifest.rejected.json");
                File.Move(ManifestPath, forensic, true);
                Logger.ZLogWarning($"Capture history original manifest quarantined. Path={forensic}");
            }

            _quarantinePending = false;
        }

        private static async Task WritePayloadAsync(string path, ReadOnlyMemory<byte> bytes,
            List<string> created, CancellationToken token)
        {
            var existed = File.Exists(path);
            if (existed && new FileInfo(path).Length == bytes.Length) return;
            await WriteAtomicAsync(path, bytes, token).ConfigureAwait(false);
            if (!existed) created.Add(path);
        }

        private static async Task WriteAtomicAsync(string path, ReadOnlyMemory<byte> bytes, CancellationToken token)
        {
            var temporary = path + ".pending";
            try
            {
                await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None,
                                 65536, true))
                    await stream.WriteAsync(bytes, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                File.Move(temporary, path, true);
            }
            finally
            {
                try
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    Logger.ZLogWarning(error, $"Capture history temporary file cleanup failed.");
                }
            }
        }

        private void DeleteUnusedFiles(HashSet<string> keep)
        {
            if (!Directory.Exists(_directory)) return;
            foreach (var path in Directory.EnumerateFiles(_directory))
            {
                var name = Path.GetFileName(path);
                if (!keep.Contains(name) && IsOwnedFile(name)) File.Delete(path);
            }
        }

        private static bool IsOwnedFile(string name)
        {
            if (name is "manifest.json" or "manifest.json.pending" or "manifest.rejected.json"
                or "manifest.rejected.json.pending")
                return true;
            if (name.EndsWith(".pending", StringComparison.Ordinal)) name = name[..^8];
            var extension = Path.GetExtension(name);
            return extension is ".png" or ".hdr" &&
                   Guid.TryParseExact(Path.GetFileNameWithoutExtension(name), "N", out _);
        }
    }
}

using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedImageManager
    {
        private bool _savingSelection;

        private async Task SaveSelectionAsync(PinnedImageWindow source)
        {
            Dispatcher.UIThread.VerifyAccess();
            if (_savingSelection || _disposed) return;
            var images = SelectionSnapshot(source);
            if (images.Length < 2 || images.Any(image => !image.CanSaveSelection)) return;
            _savingSelection = true;
            try
            {
                var profile = SavePreferencesProvider().Normalize().ManualProfile();
                var directory = profile.ResolveDirectory(OutputDirectoryProvider() ??
                                                         Environment.GetFolderPath(Environment.SpecialFolder
                                                             .MyPictures));
                var folders = await source.StorageProvider.OpenFolderPickerAsync(new()
                {
                    Title = UiText.Get("PinnedSelectionSave"), AllowMultiple = false,
                    SuggestedStartLocation = await source.StorageProvider.TryGetFolderFromPathAsync(directory),
                });
                if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } selectedDirectory) return;
                if (_disposed || images.Any(image => !image.CanSaveSelection)) return;
                using var work = CancellationTokenSource.CreateLinkedTokenSource(source.OutputLifetime);
                var token = work.Token;
                var mode = OutputColorModeProvider();
                var staging = Path.Combine(selectedDirectory, ".capture-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(staging);
                var progress = new PinnedImageBatchProgressWindow(work);
                try
                {
                    progress.Report(0, images.Length);
                    progress.Show(source);
                    var snapshots = images.Select(image => image.CaptureOutputAsync(mode, token)).ToArray();
                    var results = await Task.WhenAll(snapshots);
                    var stagedOutputs = new ScreenshotSavedOutput[results.Length];
                    var completed = 0;
                    await Parallel.ForEachAsync(Enumerable.Range(0, results.Length), new ParallelOptions
                    {
                        MaxDegreeOfParallelism = 2, CancellationToken = token,
                    }, async (index, cancellation) =>
                    {
                        var result = results[index];
                        var extension = profile.DialogExtension(result.ColorMode);
                        stagedOutputs[index] = await ScreenshotImageStorage.SaveAsync(result,
                            Path.Combine(staging, index + extension),
                            profile with { ConflictPolicy = ScreenshotConflictPolicy.Fail }, cancellation);
                        var count = Interlocked.Increment(ref completed);
                        await Dispatcher.UIThread.InvokeAsync(() => progress.Report(count, results.Length));
                    });
                    var staged = new List<string>();
                    var destinations = new List<string>();
                    var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var timestamp = DateTime.Now;
                    for (var index = 0; index < results.Length; index++)
                    {
                        var result = results[index];
                        var dimensions = PngImageCodec.ReadDimensions(result.Png);
                        var size = profile.OutputSize.Resolve(dimensions.Width, dimensions.Height);
                        var name = Path.ChangeExtension(
                            profile.CreateFileName(new ScreenshotNameContext(timestamp, Width: size.Width,
                                Height: size.Height)),
                            profile.DialogExtension(result.ColorMode));
                        var names = ReserveOutputNames(selectedDirectory, name, result.ColorMode,
                            profile.ConflictPolicy, reserved);
                        staged.AddRange(stagedOutputs[index].Paths);
                        destinations.AddRange(names);
                    }

                    await Task.Run(() => OutputBatchTransaction.Publish(staged, destinations,
                        profile.ConflictPolicy == ScreenshotConflictPolicy.Overwrite, token), token);
                    SavedPathChanged?.Invoke(destinations[0]);
                    await source.CompleteSelectionSaveAsync(destinations, profile, results[0]);
                }
                finally
                {
                    progress.Finish();
                    if (Directory.Exists(staging)) Directory.Delete(staging, true);
                }
            }
            finally
            {
                _savingSelection = false;
            }
        }

        private static string[] ReserveOutputNames(string directory, string name, OutputColorMode mode,
            ScreenshotConflictPolicy policy, HashSet<string> reserved)
        {
            var stem = Path.GetFileNameWithoutExtension(name);
            var extension = Path.GetExtension(name);
            for (var index = 0;; index++)
            {
                var suffix = index == 0 ? "" : $" ({index})";
                string[] paths = mode == OutputColorMode.Both
                    ?
                    [
                        Path.Combine(directory, stem + suffix + ".sdr.png"),
                        Path.Combine(directory, stem + suffix + ".hdr.png"),
                    ]
                    : [Path.Combine(directory, stem + suffix + extension)];
                if (paths.Any(reserved.Contains)) continue;
                if (paths.Any(Path.Exists))
                    switch (policy)
                    {
                        case ScreenshotConflictPolicy.Rename: continue;
                        case ScreenshotConflictPolicy.Fail: throw new IOException(paths.First(Path.Exists));
                        case ScreenshotConflictPolicy.Overwrite: break;
                        default: throw new ArgumentOutOfRangeException(nameof(policy));
                    }

                foreach (var path in paths) reserved.Add(path);
                return paths;
            }
        }
    }
}

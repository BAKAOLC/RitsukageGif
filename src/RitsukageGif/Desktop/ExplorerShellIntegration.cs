namespace RitsukageGif.Desktop
{
    internal sealed class ExplorerShellIntegration(IExplorerShellStore store)
    {
        private const string Owner = "RitsukageGif.Pin";
        private const string Root = @"Software\Classes\";

        private const string AllFilesPath = Root + @"*\shell\" + Owner;

        private static readonly string[] ImageExtensions =
        [
            ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".apng", ".tif", ".tiff", ".ico", ".avif", ".heic",
            ".svg",
        ];

        private static readonly string[] ImagePaths =
        [
            .. ImageExtensions.Select(extension => Root + @"SystemFileAssociations\" + extension + @"\shell\" + Owner),
        ];

        internal void Apply(ExplorerContextMenuMode mode, string executable, string caption, Action? persist = null)
        {
            var desired = mode switch
            {
                ExplorerContextMenuMode.Disabled => [],
                ExplorerContextMenuMode.ImagesOnly => ImagePaths,
                ExplorerContextMenuMode.AllFiles => [AllFilesPath],
                _ => throw new ArgumentOutOfRangeException(nameof(mode)),
            };
            var paths = ImagePaths.Append(AllFilesPath).ToArray();
            var originals = new Dictionary<string, ExplorerShellEntry?>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in paths)
            {
                var existing = store.Read(path);
                if (existing is not null && existing.Owner != Owner)
                {
                    if (desired.Contains(path, StringComparer.OrdinalIgnoreCase))
                        throw new InvalidOperationException(
                            "Explorer integration entry is owned by another application.");
                    continue;
                }

                originals.Add(path, existing);
            }

            var entry = new ExplorerShellEntry(Owner, caption,
                "\"" + executable + "\" pin-files \"%1\"", "\"" + executable + "\",0");
            var changed = new List<string>();
            try
            {
                foreach (var (path, existing) in originals)
                    if (desired.Contains(path, StringComparer.OrdinalIgnoreCase))
                    {
                        if (existing == entry) continue;
                        changed.Add(path);
                        store.Write(path, entry);
                    }
                    else if (existing is not null)
                    {
                        changed.Add(path);
                        store.Remove(path);
                    }

                persist?.Invoke();
            }
            catch (Exception failure)
            {
                var errors = new List<Exception> { failure };
                foreach (var path in changed.AsEnumerable().Reverse())
                    try
                    {
                        if (originals[path] is { } original) store.Write(path, original);
                        else store.Remove(path);
                    }
                    catch (Exception recoveryError)
                    {
                        errors.Add(recoveryError);
                    }

                if (errors.Count > 1) throw new AggregateException("Explorer integration rollback failed.", errors);
                throw;
            }
        }
    }
}

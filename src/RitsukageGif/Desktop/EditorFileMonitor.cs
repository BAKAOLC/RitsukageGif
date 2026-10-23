namespace RitsukageGif.Desktop
{
    internal sealed class EditorFileMonitor : IDisposable
    {
        private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        private readonly object _gate = new();
        private HashSet<string> _paths = new(PathComparer);
        private List<FileSystemWatcher> _watchers = [];
        private bool _disposed, _faulted;

        public event Action? Changed;

        public void SetPaths(IEnumerable<string> paths)
        {
            var normalized = paths.Select(Path.GetFullPath).ToHashSet(PathComparer);
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (!_faulted && _paths.SetEquals(normalized)) return;
                var replacements = new List<FileSystemWatcher>();
                try
                {
                    foreach (var directory in normalized.Select(Path.GetDirectoryName).Distinct(PathComparer))
                    {
                        var watcher = new FileSystemWatcher(directory!)
                        {
                            IncludeSubdirectories = false,
                            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size |
                                           NotifyFilters.CreationTime
                        };
                        replacements.Add(watcher);
                        watcher.Changed += FileChanged;
                        watcher.Created += FileChanged;
                        watcher.Deleted += FileChanged;
                        watcher.Renamed += FileRenamed;
                        watcher.Error += WatcherError;
                        watcher.EnableRaisingEvents = true;
                    }
                }
                catch
                {
                    foreach (var watcher in replacements) watcher.Dispose();
                    throw;
                }

                var previous = _watchers;
                _paths = normalized;
                _watchers = replacements;
                _faulted = false;
                foreach (var watcher in previous) watcher.Dispose();
            }
        }

        private void FileChanged(object sender, FileSystemEventArgs args)
        {
            lock (_gate)
            {
                if (_disposed || sender is not FileSystemWatcher watcher || !_watchers.Contains(watcher) ||
                    !_paths.Contains(args.FullPath)) return;
                Changed?.Invoke();
            }
        }

        private void FileRenamed(object sender, RenamedEventArgs args)
        {
            lock (_gate)
            {
                if (_disposed || sender is not FileSystemWatcher watcher || !_watchers.Contains(watcher) ||
                    !_paths.Contains(args.FullPath) && !_paths.Contains(args.OldFullPath)) return;
                Changed?.Invoke();
            }
        }

        private void WatcherError(object sender, ErrorEventArgs args)
        {
            lock (_gate)
            {
                if (_disposed || sender is not FileSystemWatcher watcher || !_watchers.Contains(watcher)) return;
                _faulted = true;
                Changed?.Invoke();
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                foreach (var watcher in _watchers) watcher.Dispose();
                _watchers.Clear();
                _paths.Clear();
                Changed = null;
            }
        }
    }
}

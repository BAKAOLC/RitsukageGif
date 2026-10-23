using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PresetEditorWindow
    {
        private readonly TreeView _workspaceTree = new();

        private readonly TextBlock _workspaceTitle = new()
        {
            FontSize = 12, FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(8, 6)
        };

        private Border? _workspacePane;
        private string? _workspacePath;
        private int _workspaceRevision;
        private FileSystemWatcher? _workspaceWatcher;
        private readonly DispatcherTimer _workspaceRefreshTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
        private bool _workspaceDisposed;
        private readonly HashSet<TreeViewItem> _workspaceLoaded = [];

        private HashSet<string> _workspaceExpanded = new(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);

        private Control CreateWorkspacePane()
        {
            if (_workspacePane is not null) return _workspacePane;
            var panel = new DockPanel();
            var heading = new DockPanel();
            var refresh = new Button
            {
                Content = "↻", FontSize = 15, MinHeight = 24, MinWidth = 24,
                Padding = new Thickness(3, 0), Margin = new Thickness(0, 3, 4, 3)
            };
            ToolTip.SetTip(refresh, UiText.Get("EditorRefreshWorkspace"));
            refresh.Click += (_, _) => RefreshWorkspace();
            DockPanel.SetDock(refresh, Dock.Right);
            heading.Children.Add(refresh);
            heading.Children.Add(_workspaceTitle);
            DockPanel.SetDock(heading, Dock.Top);
            panel.Children.Add(heading);
            panel.Children.Add(new ScrollViewer
            {
                Content = _workspaceTree, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            });
            _workspaceRefreshTimer.Tick += (_, _) =>
            {
                _workspaceRefreshTimer.Stop();
                RefreshWorkspace();
            };
            _workspacePane = new Border
            {
                Width = 200, MinWidth = 180, MaxWidth = 220, IsVisible = false,
                Background = new SolidColorBrush(Color.Parse("#F7F8FA")),
                BorderBrush = new SolidColorBrush(Color.Parse("#E2E5E9")),
                BorderThickness = new Thickness(0, 0, 1, 0), Child = panel
            };
            return _workspacePane;
        }

        private async Task OpenWorkspaceAsync()
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = UiText.Get("EditorOpenWorkspace"), AllowMultiple = false
            });
            if (_allowClose) return;
            if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path) OpenWorkspace(path);
        }

        private void OpenWorkspace(string path)
        {
            if (_workspaceDisposed || _allowClose) return;
            try
            {
                var directory = new DirectoryInfo(Path.GetFullPath(path));
                if (!directory.Exists) throw new DirectoryNotFoundException(directory.FullName);
                StopWorkspaceWatcher();
                _workspacePath = directory.FullName;
                _workspaceExpanded.Clear();
                _workspaceTree.Items.Clear();
                CreateWorkspacePane().IsVisible = true;
                _workspaceTitle.Text = directory.Name.Length == 0 ? directory.FullName : directory.Name;
                ToolTip.SetTip(_workspaceTitle, UiText.Get("EditorWorkspace") + ": " + directory.FullName);
                RefreshWorkspace();
                StartWorkspaceWatcher(directory.FullName);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                                  or ArgumentException)
            {
                _status.Text = exception.Message;
            }
        }

        private void RefreshWorkspace()
        {
            if (_workspacePath is not { } path || _allowClose || _workspaceDisposed) return;
            var expanded = new HashSet<string>(_workspaceExpanded, _workspaceExpanded.Comparer);
            RememberExpanded(_workspaceTree.Items.OfType<TreeViewItem>(), expanded);
            _workspaceExpanded = expanded;
            _workspaceLoaded.Clear();
            _workspaceTree.Items.Clear();
            var revision = ++_workspaceRevision;
            _ = Guard(() => PopulateWorkspaceAsync(path, null, revision));
        }

        private void StartWorkspaceWatcher(string path)
        {
            var watcher = new FileSystemWatcher(path)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
            };
            _workspaceWatcher = watcher;
            watcher.Created += WorkspaceChanged;
            watcher.Deleted += WorkspaceChanged;
            watcher.Renamed += WorkspaceChanged;
            watcher.Error += WorkspaceWatcherError;
            try
            {
                watcher.EnableRaisingEvents = true;
            }
            catch
            {
                StopWorkspaceWatcher();
                throw;
            }
        }

        private void WorkspaceChanged(object sender, FileSystemEventArgs args) => QueueWorkspaceRefresh(sender);

        private void WorkspaceWatcherError(object sender, ErrorEventArgs args) => QueueWorkspaceRefresh(sender);

        private void QueueWorkspaceRefresh(object sender)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_allowClose || _workspaceDisposed || !ReferenceEquals(sender, _workspaceWatcher)) return;
                _workspaceRefreshTimer.Stop();
                _workspaceRefreshTimer.Start();
            });
        }

        private void StopWorkspaceWatcher()
        {
            _workspaceRefreshTimer.Stop();
            var watcher = _workspaceWatcher;
            _workspaceWatcher = null;
            if (watcher is null) return;
            watcher.Created -= WorkspaceChanged;
            watcher.Deleted -= WorkspaceChanged;
            watcher.Renamed -= WorkspaceChanged;
            watcher.Error -= WorkspaceWatcherError;
            watcher.Dispose();
        }

        private void DisposeWorkspace()
        {
            if (_workspaceDisposed) return;
            _workspaceDisposed = true;
            ++_workspaceRevision;
            StopWorkspaceWatcher();
            _workspaceLoaded.Clear();
        }

        private static void RememberExpanded(IEnumerable<TreeViewItem> nodes, HashSet<string> expanded)
        {
            foreach (var node in nodes)
            {
                if (node.Tag is not string path) continue;
                if (node.IsExpanded) expanded.Add(path);
                else expanded.Remove(path);
                RememberExpanded(node.Items.OfType<TreeViewItem>(), expanded);
            }
        }

        private async Task PopulateWorkspaceAsync(string path, TreeViewItem? parent, int revision)
        {
            try
            {
                var entries = await Task.Run(() => new DirectoryInfo(path).EnumerateFileSystemInfos()
                    .Where(entry => (entry.Attributes & FileAttributes.Directory) != 0
                        ? (entry.Attributes & FileAttributes.ReparsePoint) == 0
                        : entry.Extension.Equals(".rgs", StringComparison.OrdinalIgnoreCase) ||
                          entry.Extension.Equals(".rgpreset", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(entry => (entry.Attributes & FileAttributes.Directory) != 0)
                    .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(entry => (entry.FullName, entry.Name,
                        Directory: (entry.Attributes & FileAttributes.Directory) != 0)).ToArray());
                if (_allowClose || _workspaceDisposed || revision != _workspaceRevision) return;
                var items = parent?.Items ?? _workspaceTree.Items;
                items.Clear();
                foreach (var entry in entries)
                {
                    var node = new TreeViewItem { Header = entry.Name, Tag = entry.FullName, FontSize = 12 };
                    ToolTip.SetTip(node, entry.FullName);
                    if (entry.Directory)
                    {
                        node.Items.Add(new TreeViewItem { Header = "…", IsEnabled = false });
                        node.Expanded += async (_, args) =>
                        {
                            if (!ReferenceEquals(args.Source, node) || !_workspaceLoaded.Add(node)) return;
                            await PopulateWorkspaceAsync(entry.FullName, node, revision);
                        };
                    }
                    else
                    {
                        node.DoubleTapped += async (_, args) =>
                        {
                            args.Handled = true;
                            await Guard(() => OpenDocumentAsync(entry.FullName));
                        };
                        node.KeyDown += async (_, args) =>
                        {
                            if (args.Key != Key.Enter) return;
                            args.Handled = true;
                            await Guard(() => OpenDocumentAsync(entry.FullName));
                        };
                    }

                    items.Add(node);
                    if (entry.Directory && _workspaceExpanded.Contains(entry.FullName)) node.IsExpanded = true;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                                  or ArgumentException)
            {
                if (_allowClose || _workspaceDisposed || revision != _workspaceRevision) return;
                if (parent is not null) _workspaceLoaded.Remove(parent);
                _status.Text = exception.Message;
            }
        }
    }
}

using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PresetEditorWindow
    {
        private readonly HashSet<TreeViewItem> _workspaceLoaded = [];
        private readonly DispatcherTimer _workspaceRefreshTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };

        private readonly TextBlock _workspaceTitle = new()
        {
            FontSize = 13, Foreground = AppTheme.Muted, FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis, Margin = new(8, 6),
        };

        private readonly TreeView _workspaceTree = new();
        private bool _workspaceDisposed;

        private HashSet<string> _workspaceExpanded = new(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);

        private Grid? _workspaceLayout;

        private Border? _workspacePane;
        private string? _workspacePath;
        private int _workspaceRevision;
        private GridSplitter? _workspaceSplitter;
        private FileSystemWatcher? _workspaceWatcher;

        private Border CreateWorkspacePane()
        {
            if (_workspacePane is not null) return _workspacePane;
            var panel = new DockPanel();
            var heading = new DockPanel();
            var refresh = new Button
            {
                Content = ToolbarIcon("EditorRefreshWorkspace"), MinHeight = 28, MinWidth = 28,
                Padding = new(3, 0), Margin = new(0, 3, 4, 3),
            };
            refresh.Classes.Add("icon-button");
            refresh.Bind(ToolTip.TipProperty, LiveText.Resource("EditorRefreshWorkspace"));
            refresh.Click += (_, _) => RefreshWorkspace();
            DockPanel.SetDock(refresh, Dock.Right);
            heading.Children.Add(refresh);
            heading.Children.Add(_workspaceTitle);
            DockPanel.SetDock(heading, Dock.Top);
            panel.Children.Add(heading);
            panel.Children.Add(new ScrollViewer
            {
                Content = _workspaceTree, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            });
            _workspaceRefreshTimer.Tick += (_, _) =>
            {
                _workspaceRefreshTimer.Stop();
                RefreshWorkspace();
            };
            _workspacePane = new()
            {
                Name = "ScriptWorkspacePane", IsVisible = false,
                Background = AppTheme.SurfaceTint,
                BorderBrush = AppTheme.Outline,
                BorderThickness = new(0, 0, 1, 0), Child = panel,
            };
            return _workspacePane;
        }

        private Grid CreateWorkspaceLayout(Control editorPane)
        {
            var layout = new Grid { Name = "ScriptWorkspaceLayout", ColumnDefinitions = new("0,0,*") };
            _workspaceLayout = layout;
            layout.ColumnDefinitions[2].MinWidth = 360;
            var workspace = CreateWorkspacePane();
            layout.Children.Add(workspace);
            _workspaceSplitter = new()
            {
                Name = "ScriptWorkspaceSplitter", IsVisible = false, Focusable = true,
                ResizeDirection = GridResizeDirection.Columns, ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
                Background = AppTheme.Outline, Cursor = new(StandardCursorType.SizeWestEast),
            };
            Grid.SetColumn(_workspaceSplitter, 1);
            layout.Children.Add(_workspaceSplitter);
            Grid.SetColumn(editorPane, 2);
            layout.Children.Add(editorPane);
            layout.SizeChanged += (_, _) => ConstrainWorkspaceWidth();
            return layout;
        }

        private void ConstrainWorkspaceWidth()
        {
            if (_workspaceLayout is not { Bounds.Width: > 0 } layout || _workspacePane is not { IsVisible: true })
                return;
            var column = layout.ColumnDefinitions[0];
            var maximum = Math.Max(160, Math.Min(480, layout.Bounds.Width - 366));
            column.MaxWidth = maximum;
            if (column.Width.IsAbsolute && column.Width.Value > maximum)
                column.Width = new(maximum);
        }

        private async Task OpenWorkspaceAsync()
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new()
            {
                Title = UiText.Get("EditorOpenWorkspace"), AllowMultiple = false,
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
                var pane = CreateWorkspacePane();
                if (!pane.IsVisible && _workspaceLayout is { } layout)
                {
                    layout.ColumnDefinitions[0].MinWidth = 160;
                    layout.ColumnDefinitions[0].Width = new(220);
                    layout.ColumnDefinitions[1].Width = new(6);
                    _workspaceSplitter!.IsVisible = true;
                }

                pane.IsVisible = true;
                ConstrainWorkspaceWidth();
                _workspaceTitle.Text = directory.Name.Length == 0 ? directory.FullName : directory.Name;
                ToolTip.SetTip(_workspaceTitle, UiText.Get("EditorWorkspace") + ": " + directory.FullName);
                RefreshWorkspace();
                StartWorkspaceWatcher(directory.FullName);
                Logger.ZLogInformation($"Script workspace opened.");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                                  or ArgumentException)
            {
                Logger.ZLogWarning(exception, $"Script workspace open failed.");
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
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
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

        private void WorkspaceChanged(object sender, FileSystemEventArgs args)
        {
            QueueWorkspaceRefresh(sender);
        }

        private void WorkspaceWatcherError(object sender, ErrorEventArgs args)
        {
            var error = args.GetException();
            Logger.ZLogError(error, $"Script workspace watcher reported an error.");
            QueueWorkspaceRefresh(sender);
        }

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
                    var node = new TreeViewItem { Header = entry.Name, Tag = entry.FullName, FontSize = 13 };
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
                Logger.ZLogWarning(exception, $"Script workspace enumeration failed.");
                _status.Text = exception.Message;
            }
        }
    }
}

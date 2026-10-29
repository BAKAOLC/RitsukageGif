using System.Collections.Concurrent;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PresetEditorWindow
    {
        private readonly TabControl _documentTabs = new()
            { MinHeight = 36, FontSize = 13, Background = AppTheme.SurfaceTint };

        private readonly List<EditorDocument> _documents = [];

        private readonly WrapPanel _fileChangeBar = new()
            { Orientation = Orientation.Horizontal, Margin = new(8, 3), IsVisible = false };

        private readonly TextBlock _fileChangeText = new() { VerticalAlignment = VerticalAlignment.Center };
        private readonly DispatcherTimer _fileChanges = new() { Interval = TimeSpan.FromMilliseconds(200) };

        private readonly EditorFileMonitor _fileMonitor = new();

        private readonly ConcurrentDictionary<string, string> _openSources =
            new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        private EditorDocument? _currentDocument;
        private bool _switchingDocument, _checkingFiles, _checkAgain;

        private StackPanel CreateDocumentHeader()
        {
            var panel = new StackPanel();
            panel.Children.Add(new ScrollViewer
            {
                Content = _documentTabs,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            });
            _fileChangeBar.Children.Add(_fileChangeText);
            var reload = new Button
                { [!ContentProperty] = LiveText.Resource("EditorReload"), Margin = new(6, 0) };
            reload.Click += async (_, _) => await Guard(async () =>
            {
                if (_currentDocument?.Path is not { } path) return;
                var document = _currentDocument;
                var text = await ReadEditorFileAsync(path);
                if (!Active && !_preparingRun && !_allowClose) ReloadDocument(document, text);
            });
            var keep = new Button { [!ContentProperty] = LiveText.Resource("EditorKeepLocal") };
            keep.Click += (_, _) =>
            {
                if (_currentDocument is not { } document || Active || _preparingRun) return;
                document.SavedText = document.ObservedText ?? "";
                document.Conflict = false;
                _savedText = document.SavedText;
                UpdateTitle();
            };
            _fileChangeBar.Children.Add(reload);
            _fileChangeBar.Children.Add(keep);
            panel.Children.Add(_fileChangeBar);
            _documentTabs.SelectionChanged += (_, _) =>
            {
                if (_preparingRun && !_switchingDocument)
                {
                    _switchingDocument = true;
                    _documentTabs.SelectedItem = _documentTabs.Items.OfType<TabItem>()
                        .FirstOrDefault(item => ReferenceEquals(item.Tag, _currentDocument));
                    _switchingDocument = false;
                    return;
                }

                if (!_switchingDocument && _documentTabs.SelectedItem is TabItem { Tag: EditorDocument document })
                    SelectDocument(document);
            };
            _fileChanges.Tick += async (_, _) =>
            {
                _fileChanges.Stop();
                await Guard(CheckExternalChangesAsync);
            };
            _fileMonitor.Changed += QueueFileCheck;
            Activated += (_, _) => QueueFileCheck();
            return panel;
        }

        private static async Task<string> ReadEditorFileAsync(string path)
        {
            if (new FileInfo(path).Length > 1024 * 1024) throw new FormatException(UiText.Get("DslTooComplex"));
            var text = await File.ReadAllTextAsync(path);
            if (text.Length > 1024 * 1024) throw new FormatException(UiText.Get("DslTooComplex"));
            return text;
        }

        private async Task OpenDocumentAsync(string path)
        {
            if (_preparingRun) return;
            path = Path.GetFullPath(path);
            var existing = _documents.FirstOrDefault(d => SameDiagnosticSource(d.Path, path));
            if (existing is not null)
            {
                SelectDocument(existing);
                return;
            }

            try
            {
                var text = await ReadEditorFileAsync(path);
                if (!_allowClose) LoadText(path, text);
            }
            catch (Exception error)
            {
                Logger.ZLogError(error, $"Script document open failed.");
                throw;
            }
        }

        internal Task OpenLocationAsync(string path)
        {
            if (!Directory.Exists(path)) return OpenDocumentAsync(path);
            OpenWorkspace(path);
            return Task.CompletedTask;
        }

        private void AddDocument(string? path, string text)
        {
            var existing = path is null ? null : _documents.FirstOrDefault(d => SameDiagnosticSource(d.Path, path));
            if (existing is not null)
            {
                SelectDocument(existing);
                return;
            }

            var document = new EditorDocument(path, text);
            if (path is not null) _openSources[path] = text;
            document.Buffer.TextChanged += (_, _) =>
            {
                if (document.Path is { } source) _openSources[source] = document.Buffer.Text;
            };
            _documents.Add(document);
            var item = new TabItem { Tag = document, Padding = new(8, 3), MinHeight = 28, FontSize = 13 };
            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            header.Children.Add(new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 180, TextTrimming = TextTrimming.CharacterEllipsis,
            });
            var close = new Button
                { Content = ToolbarIcon("EditorCloseFile"), Padding = new(4), MinWidth = 24, MinHeight = 24 };
            close.Classes.Add("icon-button");
            close.Bind(ToolTip.TipProperty, LiveText.Resource("EditorCloseFile"));
            close.Click += async (_, _) => await Guard(() => CloseDocumentAsync(document));
            header.Children.Add(close);
            item.Header = header;
            _switchingDocument = true;
            _documentTabs.Items.Add(item);
            _switchingDocument = false;
            SelectDocument(document);
            WatchDocuments();
            Logger.ZLogInformation($"Script document opened. HasFile={path is not null}, Documents={_documents.Count}");
        }

        private void SelectDocument(EditorDocument document)
        {
            if (_currentDocument == document) return;
            StoreDocumentPosition();
            _completion?.Close();
            _currentDocument = document;
            _path = document.Path;
            _savedText = document.SavedText;
            _semantic = null;
            _semanticText = null;
            _colors.Tokens = [];
            var snapshot = _debugger?.Snapshot;
            _colors.CurrentLine = snapshot is { Paused: true } && SameDiagnosticSource(snapshot.Source, document.Path)
                ? snapshot.Line
                : 0;
            _revision++;
            _analysisDiagnostics = [];
            _breakpoints.Clear();
            _breakpoints.AddRange(document.Breakpoints);
            _editor.Document = document.Buffer;
            _editor.CaretOffset = Math.Min(document.Caret, document.Buffer.TextLength);
            _editor.ScrollToHorizontalOffset(document.HorizontalOffset);
            _editor.ScrollToVerticalOffset(document.VerticalOffset);
            _switchingDocument = true;
            _documentTabs.SelectedItem =
                _documentTabs.Items.OfType<TabItem>().Single(t => ReferenceEquals(t.Tag, document));
            _switchingDocument = false;
            UpdateTitle();
            RefreshDiagnostics();
            UpdateBreakpoints();
            _analysis.Stop();
            _analysis.Start();
        }

        private void StoreDocumentPosition()
        {
            if (_currentDocument is not { } document) return;
            document.Caret = _editor.CaretOffset;
            document.HorizontalOffset = _editor.HorizontalOffset;
            document.VerticalOffset = _editor.VerticalOffset;
            document.Breakpoints.Clear();
            document.Breakpoints.AddRange(_breakpoints);
        }

        private void UpdateDocumentHeaders()
        {
            foreach (var item in _documentTabs.Items.OfType<TabItem>())
            {
                if (item.Tag is not EditorDocument document || item.Header is not StackPanel header) continue;
                ((TextBlock)header.Children[0]).Text =
                    (document.Path is null ? UiText.Get("EditorUntitled") : Path.GetFileName(document.Path)) +
                    (document.Dirty ? " *" : "") + (document.Conflict ? " !" : "");
                ToolTip.SetTip(item, document.Path);
                if (header.Children[1] is Button close) close.IsEnabled = !Active && !_preparingRun;
            }

            _fileChangeBar.IsVisible = _currentDocument?.Conflict == true;
            _fileChangeBar.IsEnabled = !Active && !_preparingRun;
            _fileChangeText.Text =
                UiText.Get(_currentDocument?.ObservedText is null ? "EditorFileMissing" : "EditorExternalChangeNotice");
        }

        private async Task CloseDocumentAsync(EditorDocument document)
        {
            if (Active || _preparingRun) return;
            SelectDocument(document);
            if (!await ConfirmDiscard()) return;
            _switchingDocument = true;
            _documentTabs.Items.Remove(_documentTabs.Items.OfType<TabItem>()
                .Single(t => ReferenceEquals(t.Tag, document)));
            _documents.Remove(document);
            if (document.Path is { } path) _openSources.TryRemove(path, out _);
            _currentDocument = null;
            _switchingDocument = false;
            if (_documents.Count == 0) AddDocument(null, "");
            else SelectDocument(_documents[^1]);
            WatchDocuments();
            Logger.ZLogInformation($"Script document closed. Documents={_documents.Count}");
        }

        private async Task<bool> ConfirmAllDocumentsAsync()
        {
            var selected = _currentDocument;
            foreach (var document in _documents.ToArray())
            {
                if (!document.Dirty) continue;
                SelectDocument(document);
                if (!await ConfirmDiscard()) return false;
            }

            if (selected is not null) SelectDocument(selected);
            return true;
        }

        private void WatchDocuments()
        {
            _fileMonitor.SetPaths(_documents.Select(d => d.Path).OfType<string>());
        }

        private void QueueFileCheck()
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_allowClose) return;
                _fileChanges.Stop();
                _fileChanges.Start();
            });
        }

        private async Task CheckExternalChangesAsync()
        {
            if (_allowClose || Active || _preparingRun) return;
            if (_checkingFiles)
            {
                _checkAgain = true;
                return;
            }

            _checkingFiles = true;
            try
            {
                foreach (var document in _documents.ToArray())
                {
                    if (document.Path is not { } path) continue;
                    string? text;
                    try
                    {
                        text = await ReadEditorFileAsync(path);
                    }
                    catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
                    {
                        text = null;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
                    {
                        _status.Text = ex.Message;
                        continue;
                    }

                    if (_allowClose || Active || _preparingRun) return;
                    if (!_documents.Contains(document) || document.Path != path) continue;
                    document.ObservedText = text;
                    if (text == document.SavedText)
                    {
                        document.Conflict = false;
                        continue;
                    }

                    if (text is not null && (!document.Dirty || document.Buffer.Text == text))
                    {
                        ReloadDocument(document, text);
                    }
                    else
                    {
                        if (!document.Conflict)
                            Logger.ZLogWarning(
                                $"Script document has an external file conflict. FileMissing={text is null}");
                        document.Conflict = true;
                    }
                }

                UpdateDocumentHeaders();
                _revision++;
                _semantic = null;
                _analysis.Start();
                WatchDocuments();
            }
            finally
            {
                _checkingFiles = false;
                if (_checkAgain)
                {
                    _checkAgain = false;
                    QueueFileCheck();
                }
            }
        }

        private void ReloadDocument(EditorDocument document, string text)
        {
            Logger.ZLogDebug($"Script document reloaded from disk. ActiveDocument={_currentDocument == document}");
            if (_currentDocument == document) StoreDocumentPosition();
            document.SavedText = text;
            document.ObservedText = text;
            document.Conflict = false;
            document.Buffer.Text = text;
            document.Buffer.UndoStack.ClearAll();
            document.Breakpoints.RemoveAll(anchor => anchor.IsDeleted);
            if (_currentDocument == document)
            {
                _savedText = text;
                _editor.CaretOffset = Math.Min(document.Caret, document.Buffer.TextLength);
                _editor.ScrollToHorizontalOffset(document.HorizontalOffset);
                _editor.ScrollToVerticalOffset(document.VerticalOffset);
                _revision++;
                _semantic = null;
                _analysis.Start();
            }

            UpdateTitle();
        }
    }
}

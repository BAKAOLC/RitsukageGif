using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System.Collections.Concurrent;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PresetEditorWindow
    {
        private readonly List<EditorDocument> _documents = [];

        private readonly ConcurrentDictionary<string, string> _openSources =
            new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        private readonly TabControl _documentTabs = new() { Height = 34, FontSize = 13 };
        private readonly EditorFileMonitor _fileMonitor = new();
        private readonly DispatcherTimer _fileChanges = new() { Interval = TimeSpan.FromMilliseconds(200) };
        private readonly TextBlock _fileChangeText = new() { VerticalAlignment = VerticalAlignment.Center };

        private readonly WrapPanel _fileChangeBar = new()
            { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 3), IsVisible = false };

        private EditorDocument? _currentDocument;
        private bool _switchingDocument, _checkingFiles, _checkAgain;

        private Control CreateDocumentHeader()
        {
            var panel = new StackPanel();
            panel.Children.Add(new ScrollViewer
            {
                Content = _documentTabs,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
            });
            _fileChangeBar.Children.Add(_fileChangeText);
            var reload = new Button { Content = UiText.Get("EditorReload"), Margin = new Thickness(6, 0) };
            reload.Click += async (_, _) => await Guard(async () =>
            {
                if (_currentDocument?.Path is not { } path) return;
                var document = _currentDocument;
                var text = await ReadEditorFileAsync(path);
                if (!Active && !_allowClose) ReloadDocument(document, text);
            });
            var keep = new Button { Content = UiText.Get("EditorKeepLocal") };
            keep.Click += (_, _) =>
            {
                if (_currentDocument is not { } document || Active) return;
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

        internal async Task OpenDocumentAsync(string path)
        {
            path = Path.GetFullPath(path);
            var existing = _documents.FirstOrDefault(d => SameDiagnosticSource(d.Path, path));
            if (existing is not null)
            {
                SelectDocument(existing);
                return;
            }

            var text = await ReadEditorFileAsync(path);
            if (!_allowClose) LoadText(path, text);
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
            var item = new TabItem { Tag = document, Padding = new Thickness(8, 3), MinHeight = 28, FontSize = 13 };
            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            header.Children.Add(new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 180, TextTrimming = TextTrimming.CharacterEllipsis
            });
            var close = new Button { Content = "×", Padding = new Thickness(3, 0), MinWidth = 18, MinHeight = 18 };
            ToolTip.SetTip(close, UiText.Get("EditorCloseFile"));
            close.Click += async (_, _) => await Guard(() => CloseDocumentAsync(document));
            header.Children.Add(close);
            item.Header = header;
            _switchingDocument = true;
            _documentTabs.Items.Add(item);
            _switchingDocument = false;
            SelectDocument(document);
            WatchDocuments();
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
            Editor.Document = document.Buffer;
            Editor.CaretOffset = Math.Min(document.Caret, document.Buffer.TextLength);
            Editor.ScrollToHorizontalOffset(document.HorizontalOffset);
            Editor.ScrollToVerticalOffset(document.VerticalOffset);
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
            document.Caret = Editor.CaretOffset;
            document.HorizontalOffset = Editor.HorizontalOffset;
            document.VerticalOffset = Editor.VerticalOffset;
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
            }

            _fileChangeBar.IsVisible = _currentDocument?.Conflict == true;
            _fileChangeBar.IsEnabled = !Active;
            _fileChangeText.Text =
                UiText.Get(_currentDocument?.ObservedText is null ? "EditorFileMissing" : "EditorExternalChangeNotice");
        }

        private async Task CloseDocumentAsync(EditorDocument document)
        {
            if (Active) return;
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

        private void WatchDocuments() => _fileMonitor.SetPaths(_documents.Select(d => d.Path).OfType<string>());

        private void QueueFileCheck() => Dispatcher.UIThread.Post(() =>
        {
            if (_allowClose) return;
            _fileChanges.Stop();
            _fileChanges.Start();
        });

        private async Task CheckExternalChangesAsync()
        {
            if (_allowClose || Active) return;
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

                    if (_allowClose || Active) return;
                    if (!_documents.Contains(document) || document.Path != path) continue;
                    document.ObservedText = text;
                    if (text == document.SavedText)
                    {
                        document.Conflict = false;
                        continue;
                    }

                    if (text is not null && (!document.Dirty || document.Buffer.Text == text))
                        ReloadDocument(document, text);
                    else document.Conflict = true;
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
                Editor.CaretOffset = Math.Min(document.Caret, document.Buffer.TextLength);
                Editor.ScrollToHorizontalOffset(document.HorizontalOffset);
                Editor.ScrollToVerticalOffset(document.VerticalOffset);
                _revision++;
                _semantic = null;
                _analysis.Start();
            }

            UpdateTitle();
        }
    }
}

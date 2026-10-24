using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Indentation.CSharp;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PresetEditorWindow : Window
    {
        internal readonly TextEditor Editor = new()
        {
            ShowLineNumbers = true, FontFamily = new("Cascadia Code,Consolas"), FontSize = 13,
            Background = AppTheme.Surface, Foreground = AppTheme.Ink,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        private readonly DispatcherTimer _analysis = new() { Interval = TimeSpan.FromMilliseconds(250) };
        private readonly List<TextAnchor> _breakpoints = [];

        private readonly ScriptColorizer _colors = new();
        private readonly List<Button> _debugButtons = [];

        private readonly ListBox _diagnostics = new() { Background = AppTheme.Surface };
        private readonly List<Button> _fileButtons = [];

        private readonly TextBox _output = new()
        {
            IsReadOnly = true, AcceptsReturn = true, FontFamily = new("Consolas"),
            BorderThickness = new(0), Background = AppTheme.Surface,
            Foreground = AppTheme.Ink, Padding = new(10, 6),
        };

        private readonly ApplicationHost _owner;

        private readonly ComboBox _presets = new()
            { MinWidth = 150, MaxWidth = 220, MinHeight = 30, Padding = new(8, 4) };

        private readonly List<Button> _runButtons = [];
        private readonly ScriptSourceResolver _sources;

        private readonly TextBlock _status = new()
        {
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = AppTheme.Muted
        };

        private readonly TextBox _variables = new()
        {
            IsReadOnly = true, AcceptsReturn = true, FontFamily = new("Consolas"),
            BorderThickness = new(0), Background = AppTheme.Surface,
            Foreground = AppTheme.Ink, Padding = new(10, 6),
        };

        private bool _allowClose, _closingEditor;
        private CompletionWindow? _completion;
        private PresetDebugger? _debugger;
        private string? _path;
        private int _revision;
        private string _savedText = "";
        private ScriptSemanticModel? _semantic;
        private string? _semanticText;
        private bool _settingsMode;

        public PresetEditorWindow(ApplicationHost owner, string? file = null)
        {
            _owner = owner;
            _sources = new(path => _openSources.GetValueOrDefault(path));
            Classes.Add("script-editor");
            FontSize = 13;
            Foreground = AppTheme.Ink;
            Background = AppTheme.Workspace;
            Width = 1000;
            Height = 720;
            MinWidth = 650;
            MinHeight = 420;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            using var iconStream =
                AssetLoader.Open(new("avares://RitsukageGif/!__AvaloniaDefaultWindowIcon"));
            Icon = new(iconStream);
            Editor.Options.ConvertTabsToSpaces = true;
            Editor.Options.IndentationSize = 4;
            Editor.Options.HighlightCurrentLine = true;
            Editor.TextArea.IndentationStrategy = new CSharpIndentationStrategy(Editor.Options);
            Editor.TextArea.TextView.LineTransformers.Add(_colors);
            var toolbar = CreateToolbar();
            var variablesPage = new ScrollViewer { Content = _variables };
            var outputPage = new ScrollViewer { Content = _output };
            var tabs = new TabControl
            {
                ItemsSource = new[]
                {
                    new TabItem
                    {
                        [!HeaderedContentControl.HeaderProperty] = LiveText.Resource("EditorProblems"),
                        Content = _diagnostics, FontSize = 13,
                        Padding = new(8, 5), MinHeight = 28,
                    },
                    new TabItem
                    {
                        [!HeaderedContentControl.HeaderProperty] = LiveText.Resource("EditorVariables"),
                        Content = variablesPage, FontSize = 13,
                        Padding = new(8, 5), MinHeight = 28,
                    },
                    new TabItem
                    {
                        [!HeaderedContentControl.HeaderProperty] = LiveText.Resource("EditorOutput"),
                        Content = outputPage, FontSize = 13,
                        Padding = new(8, 5), MinHeight = 28,
                    },
                },
            };
            var body = new Grid { RowDefinitions = new("3*,5,*"), Background = AppTheme.Surface };
            body.Children.Add(Editor);
            var splitter = new GridSplitter
            {
                ResizeDirection = GridResizeDirection.Rows, HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = AppTheme.Outline,
            };
            Grid.SetRow(splitter, 1);
            body.Children.Add(splitter);
            Grid.SetRow(tabs, 2);
            body.Children.Add(tabs);
            var root = new DockPanel { Background = AppTheme.Workspace };
            DockPanel.SetDock(toolbar, Dock.Top);
            root.Children.Add(toolbar);
            var footer = new Border
            {
                Padding = new(10, 5), Background = AppTheme.SurfaceTint,
                BorderBrush = AppTheme.Outline, BorderThickness = new(0, 1, 0, 0),
                Child = CreateProgressFooter(),
            };
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);
            var documentHeader = CreateDocumentHeader();
            DockPanel.SetDock(documentHeader, Dock.Top);
            var editorPane = new DockPanel();
            editorPane.Children.Add(documentHeader);
            editorPane.Children.Add(body);
            root.Children.Add(CreateWorkspaceLayout(editorPane));
            Content = root;
            ConfigureDiagnostics();
            Editor.TextChanged += (_, _) =>
            {
                _revision++;
                _analysis.Stop();
                _analysis.Start();
                UpdateTitle();
            };
            Editor.TextArea.Caret.PositionChanged += (_, _) => UpdateHint();
            Editor.TextArea.TextEntered += (_, e) =>
            {
                if (e.Text == ".")
                {
                    _completion?.Close();
                    Complete();
                }
                else if (e.Text is { Length: 1 } && char.IsLetter(e.Text[0]))
                {
                    Complete();
                }
            };
            AddHandler(KeyDownEvent, EditorKeyDown, RoutingStrategies.Tunnel);
            _analysis.Tick += async (_, _) =>
            {
                _analysis.Stop();
                await AnalyzeAsync();
            };
            Closing += async (_, e) =>
            {
                if (_allowClose) return;
                e.Cancel = true;
                await RequestCloseAsync();
            };
            Closed += (_, _) =>
            {
                UiText.LanguageChanged -= RefreshLanguage;
                _owner.Changed -= RefreshRecordingProgress;
                _fileMonitor.Dispose();
                _fileChanges.Stop();
                DisposeWorkspace();
                _analysis.Stop();
                _completion?.Close();
                if (_debugger is not null) _debugger.Changed -= DebugChanged;
            };
            var workspacePath = file is not null && Directory.Exists(file) ? file : null;
            if (file is not null && workspacePath is null)
            {
                _path = Path.GetFullPath(file);
                if (new FileInfo(_path).Length > 1024 * 1024) throw new FormatException(UiText.Get("DslTooComplex"));
            }

            LoadText(_path, _path is null ? "" : File.ReadAllText(_path));
            if (workspacePath is not null) OpenWorkspace(workspacePath);
            UpdateDebugControls();
            UiText.LanguageChanged += RefreshLanguage;
        }

        private bool Active => _debugger is { Snapshot.Finished: false };

        internal async Task<bool> RequestCloseAsync()
        {
            if (_allowClose) return true;
            if (_closingEditor) return false;
            _closingEditor = true;
            try
            {
                if (!await ConfirmAllDocumentsAsync()) return false;
                await StopAsync();
                _allowClose = true;
                Close();
                return true;
            }
            catch (Exception error)
            {
                _status.Text = error.Message;
                ShowExceptionDiagnostic(error);
                return false;
            }
            finally
            {
                _closingEditor = false;
            }
        }

        private async void EditorKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.Tab)
            {
                var index = _currentDocument is null ? 0 : _documents.IndexOf(_currentDocument);
                var direction = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1;
                if (_documents.Count > 0)
                    SelectDocument(_documents[(index + direction + _documents.Count) % _documents.Count]);
                e.Handled = true;
            }
            else if (!Active && e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key is Key.N or Key.O or Key.W)
            {
                e.Handled = true;
                await Guard(async () =>
                {
                    if (e.Key == Key.N)
                    {
                        LoadText(null, "");
                    }
                    else if (e.Key == Key.O)
                    {
                        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) await OpenWorkspaceAsync();
                        else await OpenAsync();
                    }
                    else if (_currentDocument is { } document)
                    {
                        await CloseDocumentAsync(document);
                    }
                });
            }
            else if (e.Key == Key.Space && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                Complete();
                e.Handled = true;
            }
            else if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                e.Handled = true;
                await Guard(async () => { await SaveAsync(e.KeyModifiers.HasFlag(KeyModifiers.Shift)); });
            }
            else if (e.Key == Key.F9)
            {
                ToggleBreakpoint();
                e.Handled = true;
            }
            else if (e.Key == Key.F10)
            {
                _debugger?.Continue(true);
                e.Handled = true;
            }
            else if (e.Key == Key.F5)
            {
                e.Handled = true;
                await Guard(Active
                    ? () =>
                    {
                        _debugger?.Continue();
                        return Task.CompletedTask;
                    }
                    : () => RunAsync(!e.KeyModifiers.HasFlag(KeyModifiers.Control)));
            }
        }

        private async Task Guard(Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception e)
            {
                _status.Text = e.Message;
                ShowExceptionDiagnostic(e);
            }
        }

        private void LoadText(string? path, string text)
        {
            AddDocument(path, text);
        }

        private void UpdateTitle()
        {
            Title = UiText.Get("EditorTitle") + " — " +
                    (_path is null ? UiText.Get("EditorUntitled") : Path.GetFileName(_path)) +
                    (Editor.Text == _savedText ? "" : " *");
            UpdateDocumentHeaders();
        }

        private async Task OpenAsync()
        {
            if (Active) return;
            var files = await StorageProvider.OpenFilePickerAsync(new()
                { Title = UiText.Get("EditorOpen"), FileTypeFilter = [TemplateType()], AllowMultiple = true });
            foreach (var file in files)
                if (file.TryGetLocalPath() is { } path)
                    await OpenDocumentAsync(path);
        }

        private static FilePickerFileType TemplateType()
        {
            return new(UiText.Get("PresetTemplate"))
                { Patterns = ["*.rgpreset", "*.rgs"] };
        }

        private async Task<bool> SaveAsync(bool saveAs = false)
        {
            if (Active) return false;
            var document = _currentDocument!;
            var path = _path;
            if (path is null || saveAs)
            {
                var file = await StorageProvider.SaveFilePickerAsync(new()
                {
                    Title = UiText.Get("EditorSave"),
                    SuggestedFileName = path is null ? "capture.rgpreset" : Path.GetFileName(path),
                    DefaultExtension = "rgpreset", FileTypeChoices = [TemplateType()], ShowOverwritePrompt = true,
                });
                if (file?.TryGetLocalPath() is not { } chosen) return false;
                path = chosen;
            }
            else
            {
                var diskText = File.Exists(path) ? await ReadEditorFileAsync(path) : null;
                if (diskText != document.SavedText &&
                    !(diskText is null && document.ObservedText is null && !document.Conflict))
                {
                    document.ObservedText = diskText;
                    document.Conflict = true;
                    UpdateDocumentHeaders();
                    throw new IOException(UiText.Get("EditorFileChanged"));
                }
            }

            if (_documents.Any(d => d != document && SameDiagnosticSource(d.Path, path)))
                throw new IOException(UiText.Get("EditorFileAlreadyOpen"));
            var text = document.Buffer.Text;
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temporary, text);
                File.Move(temporary, path, true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }

            if (document.Path is { } oldPath) _openSources.TryRemove(oldPath, out _);
            document.Path = path;
            _openSources[path] = document.Buffer.Text;
            document.SavedText = text;
            document.ObservedText = text;
            document.Conflict = false;
            if (_currentDocument == document)
            {
                _path = path;
                _savedText = text;
            }

            WatchDocuments();
            UpdateTitle();
            return true;
        }

        private async Task AnalyzeAsync()
        {
            var text = Editor.Text;
            var revision = _revision;
            var file = _path;
            var result = await Task.Run(() => (Tokens: PresetLanguage.Scan(text),
                Diagnostics: PresetLanguage.Diagnose(text, file: file),
                Model: PresetLanguage.Analyze(text, file: file, sources: _sources)));
            if (revision != _revision || _allowClose) return;
            _semantic = result.Model;
            _semanticText = text;
            _colors.Tokens = result.Tokens;
            var diagnostics = result.Diagnostics.Concat(result.Model.Diagnostics).Distinct().ToArray();
            SetDiagnostics(diagnostics);
            var selected = _presets.SelectedItem as string;
            var names = result.Model.Symbols.Where(s => s.Kind == "preset").Select(s => s.Name).ToArray();
            _presets.ItemsSource = names;
            _presets.SelectedItem = names.Contains(selected) ? selected : names.FirstOrDefault();
            UpdateBreakpoints();
            Editor.TextArea.TextView.Redraw();
            UpdateHint();
        }

        internal void Complete()
        {
            if (Editor.IsReadOnly || _completion is not null) return;
            if (_semanticText != Editor.Text || _semantic is null)
            {
                _semantic = PresetLanguage.Analyze(Editor.Text, file: _path, sources: _sources);
                _semanticText = Editor.Text;
            }

            _semantic = _semantic.Refresh();
            var entries = _semantic.Complete(Editor.CaretOffset);
            if (entries.Count == 0) return;
            var start = Editor.CaretOffset;
            while (start > 0 &&
                   (char.IsLetterOrDigit(Editor.Text[start - 1]) || Editor.Text[start - 1] == '_')) start--;
            var completion = new CompletionWindow(Editor.TextArea) { StartOffset = start };
            foreach (var entry in entries)
                completion.CompletionList.CompletionData.Add(ScriptCompletionData.Create(entry));
            _completion = completion;
            completion.Closed += (_, _) => _completion = null;
            completion.Show();
        }

        private void UpdateHint()
        {
            if (Active) return;
            _semantic = _semantic?.Refresh();
            var signature = _semanticText == Editor.Text ? _semantic?.Signature(Editor.CaretOffset) : null;
            var hint = signature is null
                ? _semanticText == Editor.Text ? _semantic?.Hint(Editor.CaretOffset) : null
                : signature.Label + (string.IsNullOrWhiteSpace(signature.Documentation)
                    ? ""
                    : "\n" + signature.Documentation);
            ToolTip.SetTip(_status, hint);
            _status.Text = UiText.Format("EditorPosition", Editor.TextArea.Caret.Line, Editor.TextArea.Caret.Column) +
                           (hint is null ? "" : "  " + hint.Split('\n')[0]);
        }

        private void ToggleBreakpoint()
        {
            var line = Editor.TextArea.Caret.Line;
            var existing = _breakpoints.FirstOrDefault(a => !a.IsDeleted && a.Line == line);
            if (existing is not null) _breakpoints.Remove(existing);
            else _breakpoints.Add(Editor.Document.CreateAnchor(Editor.Document.GetLineByNumber(line).Offset));
            UpdateBreakpoints();
            Editor.TextArea.TextView.Redraw();
        }

        private void UpdateBreakpoints()
        {
            _breakpoints.RemoveAll(a => a.IsDeleted);
            _colors.Breakpoints = _breakpoints.Select(a => a.Line).ToHashSet();
            _debugger?.SetBreakpoints(_colors.Breakpoints, _path);
        }

        private async Task RunAsync(bool debug)
        {
            if (_settingsMode)
            {
                _status.Text = UiText.Get("EditorSettingsMode");
                return;
            }

            if (Active) return;
            var selected = _currentDocument;
            foreach (var document in _documents.Where(d => d.Dirty).ToArray())
            {
                SelectDocument(document);
                if (!await SaveAsync()) return;
            }

            if (selected is not null) SelectDocument(selected);
            _runtimeDiagnostic = null;
            RefreshDiagnostics();
            await AnalyzeAsync();
            if (_problemItems.Count > 0)
            {
                await NavigateDiagnosticAsync(_problemItems[0]);
                return;
            }

            if (_presets.SelectedItem is not string name || !await SaveAsync()) return;
            if (_debugger is not null) _debugger.Changed -= DebugChanged;
            var session = new PresetDebugger { DefaultSource = _path };
            _debugger = session;
            if (debug)
            {
                session.SetBreakpoints(_colors.Breakpoints);
                session.Pause();
            }

            session.Changed += DebugChanged;
            try
            {
                _owner.StartScript(_path!, name, session);
            }
            catch
            {
                session.Complete();
                throw;
            }

            UpdateDebugControls();
        }

        private async Task StopAsync()
        {
            await _owner.StopScriptAsync(_debugger);
            _debugger?.Complete();
            UpdateDebugControls();
        }

        private void DebugChanged()
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(DebugChanged);
                return;
            }

            if (_debugger is null || _allowClose) return;
            var snapshot = _debugger.Snapshot;
            ShowRuntimeDiagnostic(snapshot);
            _variables.Text = string.Join('\n', snapshot.Variables.Select(p => p.Key + " = " + p.Value));
            _output.Text = string.Join('\n', snapshot.Output);
            var sameSource = snapshot.Source is null ||
                             string.Equals(snapshot.Source, _path, StringComparison.OrdinalIgnoreCase);
            _colors.CurrentLine = snapshot.Paused && sameSource ? snapshot.Line : 0;
            if (snapshot.Paused && sameSource && snapshot.Line > 0) Jump(snapshot.Line, snapshot.Column);
            _status.Text = snapshot.Error ??
                           UiText.Format(
                               snapshot.Finished ? "EditorFinished" :
                               snapshot.Paused ? "EditorPaused" : "EditorRunning",
                               snapshot.State ?? "", snapshot.Line);
            if (!sameSource) _status.Text += "  " + snapshot.Source;
            Editor.TextArea.TextView.Redraw();
            UpdateDebugControls();
            if (snapshot.Finished) QueueFileCheck();
        }

        private void UpdateDebugControls()
        {
            Editor.IsReadOnly = Active;
            _presets.IsEnabled = !Active;
            foreach (var b in _fileButtons) b.IsEnabled = !Active;
            foreach (var b in _runButtons) b.IsEnabled = !Active && !_settingsMode;
            foreach (var b in _debugButtons) b.IsEnabled = Active;
            foreach (var (button, key, _) in _toolbarTips)
            {
                if (key == "EditorPause") button.IsEnabled = Active && _debugger is { Snapshot.Paused: false };
                else if (key is "EditorContinue" or "EditorStep")
                    button.IsEnabled = Active && _debugger is { Snapshot.Paused: true };
            }

            UpdateDocumentHeaders();
        }

        internal void SetSettingsMode(bool enabled)
        {
            _settingsMode = enabled;
            RefreshToolbarTips();
            UpdateDebugControls();
        }

        private void Jump(int line, int column = 1)
        {
            line = Math.Clamp(line, 1, Editor.Document.LineCount);
            var documentLine = Editor.Document.GetLineByNumber(line);
            Editor.CaretOffset = documentLine.Offset + Math.Clamp(column - 1, 0, documentLine.Length);
            Editor.ScrollToLine(line);
            Editor.Focus();
        }

        private async Task<bool> ConfirmDiscard()
        {
            if (Editor.Text == _savedText) return true;
            var dialog = new Window
            {
                [!TitleProperty] = LiveText.Resource("EditorTitle"), Width = 360, SizeToContent = SizeToContent.Height,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };
            var panel = new StackPanel { Spacing = 12, Margin = new(16) };
            panel.Children.Add(new TextBlock
                { [!TextBlock.TextProperty] = LiveText.Resource("EditorUnsaved"), TextWrapping = TextWrapping.Wrap });
            var buttons = new StackPanel
                { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
            foreach (var (key, value) in new[] { ("EditorSave", 1), ("EditorDiscard", 2), ("EditorCancel", 0) })
            {
                var button = new Button { [!ContentControl.ContentProperty] = LiveText.Resource(key) };
                button.Click += (_, _) => dialog.Close(value);
                buttons.Children.Add(button);
            }

            panel.Children.Add(buttons);
            dialog.Content = panel;
            var answer = await dialog.ShowDialog<int>(this);
            return answer == 2 || (answer == 1 && await SaveAsync());
        }
    }
}

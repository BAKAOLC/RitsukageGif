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
        private static readonly ILogger Logger = LogHub.GetLogger("Desktop.ScriptEditor");

        private readonly DispatcherTimer _analysis = new() { Interval = TimeSpan.FromMilliseconds(250) };
        private readonly List<TextAnchor> _breakpoints = [];

        private readonly ScriptColorizer _colors = new();
        private readonly List<Button> _debugButtons = [];

        private readonly ListBox _diagnostics = new() { Background = AppTheme.Surface };

        private readonly TextEditor _editor = new()
        {
            ShowLineNumbers = true, FontFamily = new("Cascadia Code,Consolas"), FontSize = 13,
            Background = AppTheme.Surface, Foreground = AppTheme.Ink,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

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
            Foreground = AppTheme.Muted,
        };

        private readonly TextBox _variables = new()
        {
            IsReadOnly = true, AcceptsReturn = true, FontFamily = new("Consolas"),
            BorderThickness = new(0), Background = AppTheme.Surface,
            Foreground = AppTheme.Ink, Padding = new(10, 6),
        };

        private bool _allowClose, _closingEditor;
        private CompletionWindow? _completion;
        private bool _debugCompletionLogged;
        private PresetDebugger? _debugger;
        private string? _path;
        private bool _preparingRun, _savingDocument;
        private int _revision;
        private Task _runPreparation = Task.CompletedTask;
        private int _runPreparationVersion;
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
            _editor.Options.ConvertTabsToSpaces = true;
            _editor.Options.IndentationSize = 4;
            _editor.Options.HighlightCurrentLine = true;
            _editor.TextArea.IndentationStrategy = new CSharpIndentationStrategy(_editor.Options);
            _editor.TextArea.TextView.LineTransformers.Add(_colors);
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
            body.Children.Add(_editor);
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
            _editor.TextChanged += (_, _) =>
            {
                _revision++;
                _analysis.Stop();
                _analysis.Start();
                UpdateTitle();
            };
            _editor.TextArea.Caret.PositionChanged += (_, _) => UpdateHint();
            _editor.TextArea.TextEntered += (_, e) =>
            {
                switch (e.Text)
                {
                    case ".":
                        _completion?.Close();
                        Complete();
                        break;
                    case { Length: 1 } text when char.IsLetter(text[0]):
                        Complete();
                        break;
                }
            };
            AddHandler(KeyDownEvent, EditorKeyDown, RoutingStrategies.Tunnel);
            _analysis.Tick += async (_, _) =>
            {
                _analysis.Stop();
                await Guard(AnalyzeAsync);
            };
            Closing += async (_, e) =>
            {
                if (_allowClose) return;
                e.Cancel = true;
                await RequestCloseAsync();
            };
            Closed += (_, _) =>
            {
                Logger.ZLogInformation(
                    $"Script editor closed. Documents={_documents.Count}, DebugSessionActive={Active}");
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
            Logger.ZLogInformation(
                $"Script editor initialized. HasInitialFile={_path is not null}, HasWorkspace={workspacePath is not null}, Documents={_documents.Count}");
        }

        private bool Active => _debugger is { Snapshot.Finished: false };

        internal async Task<bool> RequestCloseAsync()
        {
            if (_allowClose) return true;
            if (_closingEditor) return false;
            Logger.ZLogDebug(
                $"Script editor close requested. Documents={_documents.Count}, RunPreparing={_preparingRun}");
            _closingEditor = true;
            try
            {
                if (_preparingRun)
                {
                    ++_runPreparationVersion;
                    await _runPreparation;
                }

                if (!await ConfirmAllDocumentsAsync())
                {
                    Logger.ZLogDebug($"Script editor close canceled during document confirmation.");
                    return false;
                }

                await StopAsync();
                _allowClose = true;
                Close();
                return true;
            }
            catch (Exception error)
            {
                Logger.ZLogError(error, $"Script editor close failed.");
                _status.Text = error.Message;
                ShowExceptionDiagnostic(error);
                return false;
            }
            finally
            {
                _closingEditor = false;
            }
        }

        private void EditorKeyDown(object? sender, KeyEventArgs e)
        {
            _ = Guard(() => HandleEditorKeyDownAsync(e));
        }

        private async Task HandleEditorKeyDownAsync(KeyEventArgs e)
        {
            if (_preparingRun && (e.Key is Key.F5 or Key.F9 or Key.F10 ||
                                  (e.KeyModifiers.HasFlag(KeyModifiers.Control) &&
                                   e.Key is Key.Tab or Key.N or Key.O or Key.W or Key.S)))
            {
                e.Handled = true;
                return;
            }

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
                    // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
                    switch (e.Key)
                    {
                        case Key.N:
                            LoadText(null, "");
                            break;
                        case Key.O:
                            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) await OpenWorkspaceAsync();
                            else await OpenAsync();
                            break;
                        case Key.W when _currentDocument is { } document:
                            await CloseDocumentAsync(document);
                            break;
                    }
                });
            }
            else
            {
                // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
                switch (e.Key)
                {
                    case Key.Space when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                        Complete();
                        e.Handled = true;
                        break;
                    case Key.S when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                        e.Handled = true;
                        await SaveAsync(e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                        break;
                    case Key.F9:
                        ToggleBreakpoint();
                        e.Handled = true;
                        break;
                    case Key.F10:
                        _debugger?.Continue(true);
                        e.Handled = true;
                        break;
                    case Key.F5:
                        e.Handled = true;
                        if (Active) _debugger?.Continue();
                        else await RunAsync(!e.KeyModifiers.HasFlag(KeyModifiers.Control));
                        break;
                }
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
                    (_editor.Text == _savedText ? "" : " *");
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
            if (_savingDocument) return false;
            _savingDocument = true;
            try
            {
                return await SaveDocumentAsync(saveAs);
            }
            catch (Exception error)
            {
                Logger.ZLogError(error, $"Script document save failed. SaveAs={saveAs}");
                throw;
            }
            finally
            {
                _savingDocument = false;
            }
        }

        private async Task<bool> SaveDocumentAsync(bool saveAs)
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
                    Logger.ZLogWarning($"Script document save blocked by an external file conflict.");
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
            Logger.ZLogInformation($"Script document saved. SaveAs={saveAs}, Documents={_documents.Count}");
            return true;
        }

        private async Task AnalyzeAsync()
        {
            var text = _editor.Text;
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
            _editor.TextArea.TextView.Redraw();
            UpdateHint();
        }

        private void Complete()
        {
            if (_editor.IsReadOnly || _completion is not null) return;
            if (_semanticText != _editor.Text || _semantic is null)
            {
                _semantic = PresetLanguage.Analyze(_editor.Text, file: _path, sources: _sources);
                _semanticText = _editor.Text;
            }

            _semantic = _semantic.Refresh();
            var entries = _semantic.Complete(_editor.CaretOffset);
            if (entries.Count == 0) return;
            var start = _editor.CaretOffset;
            while (start > 0 &&
                   (char.IsLetterOrDigit(_editor.Text[start - 1]) || _editor.Text[start - 1] == '_')) start--;
            var completion = new CompletionWindow(_editor.TextArea) { StartOffset = start };
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
            var signature = _semanticText == _editor.Text ? _semantic?.Signature(_editor.CaretOffset) : null;
            var hint = signature is null
                ? _semanticText == _editor.Text ? _semantic?.Hint(_editor.CaretOffset) : null
                : signature.Label + (string.IsNullOrWhiteSpace(signature.Documentation)
                    ? ""
                    : "\n" + signature.Documentation);
            ToolTip.SetTip(_status, hint);
            _status.Text = UiText.Format("EditorPosition", _editor.TextArea.Caret.Line, _editor.TextArea.Caret.Column) +
                           (hint is null ? "" : "  " + hint.Split('\n')[0]);
        }

        private void ToggleBreakpoint()
        {
            var line = _editor.TextArea.Caret.Line;
            var existing = _breakpoints.FirstOrDefault(a => !a.IsDeleted && a.Line == line);
            if (existing is not null) _breakpoints.Remove(existing);
            else _breakpoints.Add(_editor.Document.CreateAnchor(_editor.Document.GetLineByNumber(line).Offset));
            Logger.ZLogDebug(
                $"Script breakpoint toggled. Line={line}, Enabled={existing is null}, Breakpoints={_breakpoints.Count}");
            UpdateBreakpoints();
            _editor.TextArea.TextView.Redraw();
        }

        private void UpdateBreakpoints()
        {
            _breakpoints.RemoveAll(a => a.IsDeleted);
            _colors.Breakpoints = [.. _breakpoints.Select(a => a.Line)];
            _debugger?.SetBreakpoints(_colors.Breakpoints, _path);
        }

        private async Task RunAsync(bool debug)
        {
            if (_settingsMode)
            {
                _status.Text = UiText.Get("EditorSettingsMode");
                return;
            }

            if (Active || _preparingRun || _savingDocument || _closingEditor) return;
            var selected = _currentDocument;
            if (selected is null) return;
            var preparation = ++_runPreparationVersion;
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _runPreparation = completion.Task;
            _preparingRun = true;
            Logger.ZLogInformation(
                $"Script execution preparation started. Debug={debug}, Documents={_documents.Count}");
            UpdateDebugControls();


            ScriptDiagnostic? diagnostic = null;
            try
            {
                _runtimeDiagnostic = null;
                RefreshDiagnostics();
                await AnalyzeAsync();
                if (preparation != _runPreparationVersion || _allowClose) return;
                if (_problemItems.Length > 0)
                {
                    Logger.ZLogWarning(
                        $"Script execution blocked by diagnostics. Count={_problemItems.Length}, Line={_problemItems[0].Line}, Column={_problemItems[0].Column}");
                    diagnostic = _problemItems[0];
                    return;
                }

                if (_presets.SelectedItem is not string name)
                {
                    Logger.ZLogWarning($"Script execution blocked because no preset is selected.");
                    var message = UiText.Get("EditorNoPreset");
                    _status.Text = message;
                    _runtimeDiagnostic = new(1, 1, message, _path);
                    RefreshDiagnostics();
                    return;
                }

                foreach (var document in _documents.Where(d => d.Dirty).ToArray())
                {
                    SelectDocument(document);
                    if (!await SaveAsync() || preparation != _runPreparationVersion || _allowClose) return;
                }

                SelectDocument(selected);
                if (!await SaveAsync() || preparation != _runPreparationVersion || _allowClose) return;
                var path = selected.Path;
                if (path is null) return;
                await AnalyzeAsync();
                if (preparation != _runPreparationVersion || _allowClose) return;
                if (_problemItems.Length > 0)
                {
                    Logger.ZLogWarning(
                        $"Saved script execution blocked by diagnostics. Count={_problemItems.Length}, Line={_problemItems[0].Line}, Column={_problemItems[0].Column}");
                    diagnostic = _problemItems[0];
                    return;
                }

                var session = new PresetDebugger { DefaultSource = path };
                if (debug)
                {
                    session.SetBreakpoints(_colors.Breakpoints);
                    session.Pause();
                }

                try
                {
                    _owner.StartScript(path, name, session);
                }
                catch
                {
                    session.Complete();
                    throw;
                }

                if (_debugger is not null) _debugger.Changed -= DebugChanged;
                _debugCompletionLogged = false;
                _debugger = session;
                session.Changed += DebugChanged;
                Logger.ZLogInformation(
                    $"Script execution session started. Debug={debug}, Breakpoints={_colors.Breakpoints.Count}");
                DebugChanged();
            }
            catch (Exception error)
            {
                LogExecutionPreparationFailure(error, debug);
                throw;
            }
            finally
            {
                _preparingRun = false;
                Logger.ZLogDebug(
                    $"Script execution preparation ended. Debug={debug}, VersionCurrent={preparation == _runPreparationVersion}, Closing={_allowClose}");
                if (_documents.Contains(selected)) SelectDocument(selected);
                UpdateDebugControls();
                completion.SetResult();
                QueueFileCheck();
                if (diagnostic is not null && !_closingEditor) await NavigateDiagnosticAsync(diagnostic);
            }
        }

        private static void LogExecutionPreparationFailure(Exception error, bool debug)
        {
            switch (error)
            {
                case PresetSyntaxException syntax:
                    Logger.ZLogWarning(
                        $"Script execution preparation rejected a syntax diagnostic. Debug={debug}, Line={syntax.Line}, Column={syntax.Column}");
                    break;
                case ScriptExecutionException execution:
                    Logger.ZLogWarning(
                        $"Script execution preparation rejected an execution diagnostic. Debug={debug}, Line={execution.Line}, Column={execution.Column}");
                    break;
                default:
                    Logger.ZLogError(error, $"Script execution preparation failed. Debug={debug}");
                    break;
            }
        }

        private async Task StopAsync()
        {
            Logger.ZLogDebug($"Script execution stop requested. SessionActive={Active}, RunPreparing={_preparingRun}");
            if (_preparingRun) ++_runPreparationVersion;
            try
            {
                await _owner.StopScriptAsync(_debugger);
                _debugger?.Complete();
                UpdateDebugControls();
            }
            catch (Exception error)
            {
                Logger.ZLogError(error, $"Script execution stop failed.");
                throw;
            }

            Logger.ZLogDebug($"Script execution stop completed.");
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
            if (snapshot.Finished && !_debugCompletionLogged)
            {
                _debugCompletionLogged = true;
                if (snapshot.Error is not null)
                    Logger.ZLogWarning(
                        $"Script execution session failed. Line={snapshot.Line}, Column={snapshot.Column}");
                else
                    Logger.ZLogInformation(
                        $"Script execution session finished. Line={snapshot.Line}, Column={snapshot.Column}");
            }

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
            _editor.TextArea.TextView.Redraw();
            UpdateDebugControls();
            if (snapshot.Finished) QueueFileCheck();
        }

        private void UpdateDebugControls()
        {
            _editor.IsReadOnly = Active || _preparingRun;
            _presets.IsEnabled = !Active && !_preparingRun;
            foreach (var b in _fileButtons) b.IsEnabled = !Active && !_preparingRun;
            foreach (var b in _runButtons) b.IsEnabled = !Active && !_preparingRun && !_settingsMode;
            foreach (var b in _debugButtons) b.IsEnabled = Active;
            foreach (var (button, key, _) in _toolbarTips)
                button.IsEnabled = key switch
                {
                    "EditorStop" => Active || _preparingRun,
                    "EditorPause" => Active && _debugger is { Snapshot.Paused: false },
                    "EditorContinue" or "EditorStep" => Active && _debugger is { Snapshot.Paused: true },
                    _ => button.IsEnabled,
                };

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
            line = Math.Clamp(line, 1, _editor.Document.LineCount);
            var documentLine = _editor.Document.GetLineByNumber(line);
            _editor.CaretOffset = documentLine.Offset + Math.Clamp(column - 1, 0, documentLine.Length);
            _editor.ScrollToLine(line);
            _editor.Focus();
        }

        private async Task<bool> ConfirmDiscard()
        {
            if (_editor.Text == _savedText) return true;
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
                var button = new Button { [!ContentProperty] = LiveText.Resource(key) };
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

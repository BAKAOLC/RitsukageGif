using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Indentation.CSharp;

namespace RitsukageGif.Desktop
{
    internal sealed class ActionScriptEditor : Grid, IDisposable
    {
        private readonly DispatcherTimer _analysisTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
        private readonly ScriptColorizer _colors = new();

        private readonly TextEditor _editor = new()
        {
            Name = "ActionScriptText", ShowLineNumbers = true,
            FontFamily = new("Cascadia Code,Consolas"), FontSize = 13,
            Background = AppTheme.Surface, Foreground = AppTheme.Ink,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        private readonly TextBlock _hint = new() { Foreground = AppTheme.Muted, TextWrapping = TextWrapping.Wrap };
        private readonly ListBox _problems = new() { Name = "ActionScriptProblems", MaxHeight = 92 };
        private CompletionWindow? _completion;
        private bool _disposed;
        private int _revision;
        private ScriptSemanticModel? _semantic;

        internal ActionScriptEditor()
        {
            RowDefinitions = new("*,Auto,Auto");
            MinHeight = 160;
            _problems.ItemTemplate = new FuncDataTemplate<ScriptDiagnostic?>((diagnostic, _) => new TextBlock
            {
                Text = diagnostic is null ? "" : ActionScriptCompilation.FormatDiagnostic(diagnostic),
                TextWrapping = TextWrapping.Wrap,
            });
            Children.Add(_editor);
            SetRow(_hint, 1);
            Children.Add(_hint);
            SetRow(_problems, 2);
            Children.Add(_problems);
            _editor.Options.ConvertTabsToSpaces = true;
            _editor.Options.IndentationSize = 4;
            _editor.Options.HighlightCurrentLine = true;
            _editor.TextArea.IndentationStrategy = new CSharpIndentationStrategy(_editor.Options);
            _editor.TextArea.TextView.LineTransformers.Add(_colors);
            _editor.TextChanged += (_, _) =>
            {
                ++_revision;
                _semantic = null;
                _colors.Diagnostics = [];
                _problems.ItemsSource = null;
                _problems.IsVisible = false;
                ToolTip.SetTip(_editor, null);
                ToolTip.SetTip(_hint, null);
                RefreshHint();
                _analysisTimer.Stop();
                _analysisTimer.Start();
                SourceChanged?.Invoke();
            };
            _analysisTimer.Tick += Analyze;
            _editor.TextArea.TextEntered += (_, args) =>
            {
                if (args.Text == ".") Complete();
            };
            _editor.KeyDown += (_, args) =>
            {
                if (args.Key != Key.Space || !args.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
                args.Handled = true;
                Complete();
            };
            _editor.TextArea.Caret.PositionChanged += (_, _) => RefreshHint();
            _problems.SelectionChanged += (_, _) =>
            {
                if (_problems.SelectedItem is ScriptDiagnostic diagnostic) Jump(diagnostic);
            };
            ActualThemeVariantChanged += (_, _) => _editor.TextArea.TextView.Redraw();
        }

        internal string Text
        {
            get => _editor.Text;
            set => _editor.Text = value;
        }

        internal bool IsReadOnly
        {
            get => _editor.IsReadOnly;
            set
            {
                _editor.IsReadOnly = value;
                if (value) _completion?.Close();
            }
        }

        public void Dispose()
        {
            _disposed = true;
            ++_revision;
            _analysisTimer.Stop();
            _analysisTimer.Tick -= Analyze;
            _completion?.Close();
        }

        internal event Action? SourceChanged;

        internal void ShowDiagnostic(ScriptDiagnostic? diagnostic)
        {
            _colors.Diagnostics = diagnostic is null ? _semantic?.Diagnostics ?? [] : [diagnostic];
            _problems.ItemsSource = _colors.Diagnostics;
            _problems.IsVisible = _colors.Diagnostics.Count > 0;
            _editor.TextArea.TextView.Redraw();
            if (diagnostic is not null) Jump(diagnostic);
        }

        private void Analyze(object? sender, EventArgs args)
        {
            _ = AnalyzeAsync();
        }

        private async Task AnalyzeAsync()
        {
            _analysisTimer.Stop();
            var revision = _revision;
            var text = Text;
            try
            {
                var model = await Task.Run(() => PresetLanguage.Analyze(text, ActionScriptLibrary.CreateBindings()));
                if (_disposed || revision != _revision) return;
                _semantic = model;
                _colors.Tokens = PresetLanguage.Scan(text);
                ShowDiagnostic(null);
                RefreshHint();
            }
            catch (Exception error)
            {
                if (!_disposed && revision == _revision) ShowDiagnostic(ActionScriptCompilation.Diagnostic(error));
            }
        }

        private void Complete()
        {
            if (_editor.IsReadOnly || _completion is not null || _disposed) return;
            var source = Text;
            var offset = _editor.CaretOffset;
            try
            {
                _semantic = PresetLanguage.Analyze(source, ActionScriptLibrary.CreateBindings());
                OpenCompletion(_semantic, source, offset);
            }
            catch (Exception error)
            {
                ShowDiagnostic(ActionScriptCompilation.Diagnostic(error));
            }
        }

        private void OpenCompletion(ScriptSemanticModel model, string source, int offset)
        {
            var entries = model.Complete(offset);
            if (entries.Count == 0) return;
            var start = offset;
            while (start > 0 && (char.IsLetterOrDigit(source[start - 1]) || source[start - 1] == '_')) --start;
            var completion = new CompletionWindow(_editor.TextArea) { StartOffset = start };
            foreach (var entry in entries)
                completion.CompletionList.CompletionData.Add(ScriptCompletionData.Create(entry));
            _completion = completion;
            completion.Closed += (_, _) => _completion = null;
            completion.Show();
        }

        private void RefreshHint()
        {
            var signature = _semantic?.Signature(_editor.CaretOffset);
            var hint = signature?.Label ?? _semantic?.Hint(_editor.CaretOffset);
            _hint.Text = UiText.Format("EditorPosition", _editor.TextArea.Caret.Line, _editor.TextArea.Caret.Column);
            ToolTip.SetTip(_editor, hint);
            ToolTip.SetTip(_hint, hint);
        }

        private void Jump(ScriptDiagnostic diagnostic)
        {
            if (!ActionScriptCompilation.IsEntryDiagnostic(diagnostic)) return;
            var line = Math.Clamp(diagnostic.Line, 1, _editor.Document.LineCount);
            var column = Math.Clamp(diagnostic.Column, 1, _editor.Document.GetLineByNumber(line).Length + 1);
            _editor.TextArea.Caret.Location = new(line, column);
            _editor.ScrollTo(line, column);
        }
    }
}

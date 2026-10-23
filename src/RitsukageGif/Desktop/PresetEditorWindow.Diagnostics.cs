using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PresetEditorWindow
    {
        private IReadOnlyList<ScriptDiagnostic> _problemItems = [];
        private IReadOnlyList<ScriptDiagnostic> _analysisDiagnostics = [];
        private ScriptDiagnostic? _runtimeDiagnostic;

        private void ConfigureDiagnostics()
        {
            _diagnostics.ItemTemplate = new FuncDataTemplate<ScriptDiagnostic?>((diagnostic, _) =>
            {
                if (diagnostic is null) return new TextBlock();
                var panel = new Grid
                    { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), Margin = new Thickness(4, 2) };
                var file = new TextBlock
                {
                    Text = diagnostic.Source is null
                        ? UiText.Get("EditorUntitled")
                        : Path.GetFileName(diagnostic.Source),
                    TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 180,
                    Margin = new Thickness(0, 0, 10, 0)
                };
                ToolTip.SetTip(file, diagnostic.Source);
                panel.Children.Add(file);
                var position = new TextBlock
                {
                    Text = UiText.Format("EditorPosition", diagnostic.Line, diagnostic.Column),
                    Margin = new Thickness(0, 0, 10, 0)
                };
                Grid.SetColumn(position, 1);
                panel.Children.Add(position);
                var message = new TextBlock { Text = diagnostic.Message, TextWrapping = TextWrapping.Wrap };
                Grid.SetColumn(message, 2);
                panel.Children.Add(message);
                return panel;
            });
            _diagnostics.Tapped += async (_, _) => await Guard(NavigateSelectedDiagnosticAsync);
            _diagnostics.KeyDown += async (_, e) =>
            {
                if (e.Key != Key.Enter) return;
                e.Handled = true;
                await Guard(NavigateSelectedDiagnosticAsync);
            };
        }

        private Task NavigateSelectedDiagnosticAsync() => _diagnostics.SelectedItem is ScriptDiagnostic diagnostic
            ? NavigateDiagnosticAsync(diagnostic)
            : Task.CompletedTask;

        private async Task NavigateDiagnosticAsync(ScriptDiagnostic diagnostic)
        {
            var source = DiagnosticSource(diagnostic.Source);
            if (source is not null && !SameDiagnosticSource(source, _path))
            {
                if (_allowClose) return;
                await OpenDocumentAsync(source);
                RefreshDiagnostics();
            }

            if (!_allowClose) Jump(diagnostic.Line, diagnostic.Column);
        }

        private void SetDiagnostics(IReadOnlyList<ScriptDiagnostic> diagnostics)
        {
            _analysisDiagnostics = diagnostics.Select(diagnostic =>
                diagnostic with { Source = DiagnosticSource(diagnostic.Source) ?? _path }).DistinctBy(diagnostic =>
                (diagnostic.Source, diagnostic.Line, diagnostic.Column, diagnostic.Message)).ToArray();
            RefreshDiagnostics();
        }

        private void ShowExceptionDiagnostic(Exception exception)
        {
            var diagnostic = exception switch
            {
                PresetSyntaxException syntax => syntax.Diagnostic,
                ScriptExecutionException execution => execution.Diagnostic,
                _ => null
            };
            if (diagnostic is null) return;
            _runtimeDiagnostic = diagnostic with { Source = DiagnosticSource(diagnostic.Source) ?? _path };
            RefreshDiagnostics();
        }

        private void ShowRuntimeDiagnostic(PresetDebugSnapshot snapshot)
        {
            if (snapshot.Error is null) return;
            var diagnostic = snapshot.ErrorDiagnostic ?? new ScriptDiagnostic(Math.Max(1, snapshot.Line),
                Math.Max(1, snapshot.Column),
                snapshot.Error, snapshot.Source ?? _path);
            if (_runtimeDiagnostic == diagnostic) return;
            _runtimeDiagnostic = diagnostic;
            RefreshDiagnostics();
        }

        private void RefreshDiagnostics()
        {
            _problemItems = ExpandDiagnostics(_runtimeDiagnostic is null
                ? _analysisDiagnostics
                : _analysisDiagnostics.Append(_runtimeDiagnostic)).ToArray();
            _diagnostics.ItemsSource = _problemItems;
            _colors.Diagnostics = _problemItems.Where(diagnostic => SameDiagnosticSource(diagnostic.Source, _path))
                .ToArray();
        }

        private IEnumerable<ScriptDiagnostic> ExpandDiagnostics(IEnumerable<ScriptDiagnostic> diagnostics)
        {
            var pending = new Queue<(ScriptDiagnostic Diagnostic, string? Source)>(
                diagnostics.Select(diagnostic => (diagnostic, _path)));
            var visited = new HashSet<ScriptDiagnostic>(ReferenceEqualityComparer.Instance);
            while (pending.TryDequeue(out var entry))
            {
                if (!visited.Add(entry.Diagnostic)) continue;
                var source = entry.Diagnostic.Source is { } path
                    ? Path.GetFullPath(path,
                        entry.Source is null
                            ? Environment.CurrentDirectory
                            : Path.GetDirectoryName(Path.GetFullPath(entry.Source))!)
                    : entry.Source;
                yield return entry.Diagnostic with { Source = source };
                foreach (var related in entry.Diagnostic.Related) pending.Enqueue((related, source));
            }
        }

        private string? DiagnosticSource(string? source) => source is null
            ? null
            : Path.GetFullPath(source,
                _path is null ? Environment.CurrentDirectory : Path.GetDirectoryName(Path.GetFullPath(_path))!);

        private static bool SameDiagnosticSource(string? left, string? right) =>
            string.Equals(left, right,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}

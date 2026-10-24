using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PresetEditorWindow
    {
        private readonly List<(Button Button, string Key, string? Shortcut)> _toolbarTips = [];

        internal void RefreshLanguage()
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(RefreshLanguage);
                return;
            }

            if (_allowClose) return;
            UpdateTitle();
            RefreshToolbarTips();
            if (_workspacePath is { } path)
                ToolTip.SetTip(_workspaceTitle, UiText.Get("EditorWorkspace") + ": " + path);
            var diagnostic = _diagnostics.SelectedItem;
            RefreshDiagnostics();
            _diagnostics.SelectedItem = diagnostic;
            RefreshRecordingProgress();
            // AvaloniaEdit resolves its search labels when applying the template.
            // Keep the panel (and its query/options) while recreating only that visual tree.
            if (Editor.SearchPanel is { Template: { } template } search)
            {
                search.SetCurrentValue(TemplatedControl.TemplateProperty, null);
                search.ApplyTemplate();
                search.SetCurrentValue(TemplatedControl.TemplateProperty, template);
                search.ApplyTemplate();
            }

            if (_debugger is { } debugger)
            {
                var snapshot = debugger.Snapshot;
                _status.Text = snapshot.Error ?? UiText.Format(
                    snapshot.Finished ? "EditorFinished" : snapshot.Paused ? "EditorPaused" : "EditorRunning",
                    snapshot.State ?? "", snapshot.Line);
                if (snapshot.Source is { } source && !SameDiagnosticSource(source, _path))
                    _status.Text += "  " + source;
            }
            else
            {
                UpdateHint();
            }

            // Recompute localized analysis without replacing documents, caret positions or undo history.
            _semantic = null;
            _semanticText = null;
            _completion?.Close();
            _analysis.Stop();
            _analysis.Start();
        }

        private void RefreshToolbarTips()
        {
            foreach (var (button, key, shortcut) in _toolbarTips)
                RefreshToolbarTip(button, key, shortcut);
        }

        private void RefreshToolbarTip(Button button, string key, string? shortcut)
        {
            var text = _settingsMode && key is "EditorRun" or "EditorDebug"
                ? UiText.Get("EditorSettingsMode")
                : UiText.Get(key) + (shortcut is null ? "" : " (" + shortcut + ")");
            ToolTip.SetTip(button, text);
        }
    }
}

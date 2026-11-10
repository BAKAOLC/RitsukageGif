using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Path = Avalonia.Controls.Shapes.Path;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PresetEditorWindow
    {
        private Border CreateToolbar()
        {
            var fileTools = new WrapPanel { Orientation = Orientation.Horizontal };
            var executionTools = new WrapPanel { Orientation = Orientation.Horizontal };

            Add(fileTools, "EditorNew", () =>
            {
                LoadText(null, "");
                return Task.CompletedTask;
            }, _fileButtons, "Ctrl+N");
            Add(fileTools, "EditorOpen", OpenAsync, _fileButtons, "Ctrl+O");
            Add(fileTools, "EditorOpenWorkspace", OpenWorkspaceAsync, _fileButtons, "Ctrl+Shift+O");
            Add(fileTools, "EditorSave", async () => { await SaveAsync(); }, _fileButtons, "Ctrl+S");
            Add(fileTools, "EditorSaveAs", async () => { await SaveAsync(true); }, _fileButtons, "Ctrl+Shift+S");
            _presets.Margin = new(0, 2, 8, 2);
            executionTools.Children.Add(_presets);
            Add(executionTools, "EditorRun", () => RunAsync(false), _runButtons, "Ctrl+F5", true);
            Add(executionTools, "EditorDebug", () => RunAsync(true), _runButtons, "F5");
            Add(executionTools, "EditorPause", () =>
            {
                _debugger?.Pause();
                return Task.CompletedTask;
            }, _debugButtons);
            Add(executionTools, "EditorContinue", () =>
            {
                _debugger?.Continue();
                return Task.CompletedTask;
            }, _debugButtons, "F5");
            Add(executionTools, "EditorStep", () =>
            {
                _debugger?.Continue(true);
                return Task.CompletedTask;
            }, _debugButtons, "F10");
            Add(executionTools, "EditorStop", StopAsync, _debugButtons);
            Add(executionTools, "EditorBreakpoint", () =>
            {
                ToggleBreakpoint();
                return Task.CompletedTask;
            }, shortcut: "F9");
            var rows = new StackPanel();
            rows.Children.Add(fileTools);
            rows.Children.Add(new Border
            {
                Child = executionTools, BorderBrush = AppTheme.Outline,
                BorderThickness = new(0, 1, 0, 0), Padding = new(0, 3, 0, 0), Margin = new(0, 3, 0, 0),
            });
            return new()
            {
                Child = rows, Padding = new(8, 4), Background = AppTheme.Surface,
                BorderBrush = AppTheme.Outline, BorderThickness = new(0, 0, 0, 1),
            };

            void Add(Panel panel, string key, Func<Task> action, List<Button>? group = null,
                string? shortcut = null, bool primary = false)
            {
                var content = new StackPanel
                    { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
                content.Children.Add(ToolbarIcon(key, primary ? Brushes.White : AppTheme.Muted));
                content.Children.Add(new TextBlock
                {
                    [!TextBlock.TextProperty] = LiveText.Resource(key), VerticalAlignment = VerticalAlignment.Center,
                });
                var button = new Button
                {
                    Content = content, MinHeight = 30, Padding = new(8, 4),
                    Margin = new(0, 2, 4, 2), FontSize = 13,
                    VerticalContentAlignment = VerticalAlignment.Center,
                };
                button.Classes.Add(primary ? "primary" : "tool");
                _toolbarTips.Add((button, key, shortcut));
                RefreshToolbarTip(button, key, shortcut);
                button.Click += async (_, _) => await Guard(action);
                panel.Children.Add(button);
                group?.Add(button);
            }
        }

        private static Path ToolbarIcon(string key, IBrush? brush = null)
        {
            var geometry = key switch
            {
                "EditorNew" => "PresetEditorNew",
                "EditorOpen" => "PresetEditorOpen",
                "EditorOpenWorkspace" => "PresetEditorOpenWorkspace",
                "EditorSave" =>
                    "PresetEditorSave",
                "EditorSaveAs" =>
                    "PresetEditorSaveAs",
                "EditorRun" or "EditorContinue" => "PresetEditorRun",
                "EditorDebug" =>
                    "PresetEditorDebug",
                "EditorPause" => "PresetEditorPause",
                "EditorStep" => "PresetEditorStep",
                "EditorStop" => "PresetEditorStop",
                "EditorBreakpoint" => "PresetEditorBreakpoint",
                "EditorCloseFile" => "PresetEditorCloseFile",
                "EditorRefreshWorkspace" => "PresetEditorRefreshWorkspace",
                _ => "PresetEditorFallbackStop",
            };
            return new()
            {
                Data = IconCatalog.GetGeometry(geometry), Stroke = brush ?? AppTheme.Muted,
                StrokeThickness = 1.5, Width = 16, Height = 16,
                Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center,
            };
        }
    }
}

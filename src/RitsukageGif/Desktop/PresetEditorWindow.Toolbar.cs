using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PresetEditorWindow
    {
        private Control CreateToolbar()
        {
            var fileTools = new WrapPanel { Orientation = Orientation.Horizontal };
            var executionTools = new WrapPanel { Orientation = Orientation.Horizontal };

            void Add(Panel panel, string key, Func<Task> action, List<Button>? group = null,
                string? shortcut = null, bool primary = false)
            {
                var content = new StackPanel
                    { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
                content.Children.Add(ToolbarIcon(key, primary ? Brushes.White : AppTheme.Muted));
                content.Children.Add(new TextBlock
                {
                    [!TextBlock.TextProperty] = LiveText.Resource(key), VerticalAlignment = VerticalAlignment.Center
                });
                var button = new Button
                {
                    Content = content, MinHeight = 30, Padding = new(8, 4),
                    Margin = new(0, 2, 4, 2), FontSize = 13,
                    VerticalContentAlignment = VerticalAlignment.Center
                };
                button.Classes.Add(primary ? "primary" : "tool");
                _toolbarTips.Add((button, key, shortcut));
                RefreshToolbarTip(button, key, shortcut);
                button.Click += async (_, _) => await Guard(action);
                panel.Children.Add(button);
                group?.Add(button);
            }

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
                BorderThickness = new(0, 1, 0, 0), Padding = new(0, 3, 0, 0), Margin = new(0, 3, 0, 0)
            });
            return new Border
            {
                Child = rows, Padding = new(8, 4), Background = AppTheme.Surface,
                BorderBrush = AppTheme.Outline, BorderThickness = new(0, 0, 0, 1)
            };
        }

        private static Control ToolbarIcon(string key, IBrush? brush = null)
        {
            var geometry = key switch
            {
                "EditorNew" => "M3,1 L9,1 13,5 13,15 3,15 Z M9,1 L9,5 13,5 M5,9 L11,9 M8,6 L8,12",
                "EditorOpen" => "M1,5 L1,3 6,3 8,5 15,5 13,13 1,13 Z M1,7 L13,7",
                "EditorOpenWorkspace" => "M1,5 L1,3 6,3 8,5 15,5 15,14 1,14 Z M4,8 L12,8 12,12 4,12 Z M7,8 L7,12",
                "EditorSave" =>
                    "M2,1 L12,1 14,3 14,15 2,15 Z M5,1 L5,6 11,6 11,1 M5,15 L5,10 11,10 11,15",
                "EditorSaveAs" =>
                    "M7,14 L2,14 2,1 11,1 13,3 13,6 M5,1 L5,5 10,5 10,1 M5,14 L5,9 8,9 M8,15 L9,12 13,8 15,10 11,14 Z M12,9 L14,11",
                "EditorRun" or "EditorContinue" => "M4,2 L13,8 4,14 Z",
                "EditorDebug" =>
                    "M5,5 L11,5 11,11 Q8,15 5,11 Z M6,2 L10,2 10,5 M2,7 L5,7 M11,7 L14,7 M2,11 L5,10 M11,10 L14,11 M3,3 L5,5 M11,5 L13,3",
                "EditorPause" => "M5,2 L5,14 M11,2 L11,14",
                "EditorStep" => "M2,3 L9,8 2,13 Z M12,2 L12,14",
                "EditorStop" => "M3,3 L13,3 13,13 3,13 Z",
                "EditorBreakpoint" => "M8,2 A6,6 0 1 1 8,14 A6,6 0 1 1 8,2 Z",
                "EditorCloseFile" => "M4,4 L12,12 M12,4 L4,12",
                "EditorRefreshWorkspace" => "M13,7 A5,5 0 1 0 13,11 M13,2 L13,7 8,7",
                _ => "M3,3 L13,3 13,13 3,13 Z"
            };
            return new Avalonia.Controls.Shapes.Path
            {
                Data = Geometry.Parse(geometry), Stroke = brush ?? AppTheme.Muted,
                StrokeThickness = 1.5, Width = 16, Height = 16,
                Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center
            };
        }
    }
}

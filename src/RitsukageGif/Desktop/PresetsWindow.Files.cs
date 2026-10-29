using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PresetsWindow
    {
        private readonly Dictionary<string, string> _fileErrors = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string[]> _filePresetNames = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<(string Name, TextBlock Label)> _shortcutLabels = [];

        private void ReadFileDetails()
        {
            _filePresetNames.Clear();
            _fileErrors.Clear();
            _shortcutLabels.Clear();
            foreach (var path in _files)
                try
                {
                    _filePresetNames[path] = [.. PresetCatalog.Load([path]).Select(preset => preset.Name)];
                }
                catch (Exception error)
                {
                    _fileErrors[path] = error.Message;
                }
        }

        private StackPanel CreateFileCard(string path)
        {
            var panel = new StackPanel { Spacing = 4, Margin = new(4, 6) };
            panel.Children.Add(new TextBlock
            {
                Text = Path.GetFileName(path), FontWeight = FontWeight.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            var location = new TextBlock
            {
                Text = path, FontSize = 12, Foreground = AppTheme.Muted,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            ToolTip.SetTip(location, path);
            panel.Children.Add(location);
            if (_filePresetNames.TryGetValue(path, out var names))
                foreach (var name in names)
                {
                    var row = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 12 };
                    row.Children.Add(new TextBlock { Text = name, TextWrapping = TextWrapping.Wrap });
                    var shortcuts = new TextBlock
                    {
                        Text = ShortcutSummary(name), FontSize = 12, Foreground = AppTheme.Muted,
                        VerticalAlignment = VerticalAlignment.Center,
                    };
                    Grid.SetColumn(shortcuts, 1);
                    row.Children.Add(shortcuts);
                    _shortcutLabels.Add((name, shortcuts));
                    shortcuts.AttachedToVisualTree += (_, _) =>
                    {
                        if (!_shortcutLabels.Contains((name, shortcuts))) _shortcutLabels.Add((name, shortcuts));
                        shortcuts.Text = ShortcutSummary(name);
                    };
                    shortcuts.DetachedFromVisualTree += (_, _) => _shortcutLabels.Remove((name, shortcuts));
                    panel.Children.Add(row);
                }

            if (_fileErrors.TryGetValue(path, out var error))
                panel.Children.Add(new TextBlock
                {
                    Text = error, Foreground = AppTheme.Error, FontSize = 12, TextWrapping = TextWrapping.Wrap,
                });
            return panel;
        }

        private string ShortcutSummary(string name)
        {
            return string.Join(", ", _entries.Where(entry => entry.Preset.SelectedItem as string == name)
                .Select(entry => entry.Shortcut.Text).Where(shortcut => !string.IsNullOrWhiteSpace(shortcut)));
        }

        private void RefreshShortcutLabels()
        {
            foreach (var (name, label) in _shortcutLabels) label.Text = ShortcutSummary(name);
        }
    }
}

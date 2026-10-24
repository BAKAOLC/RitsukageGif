using System.Text;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace RitsukageGif.Desktop
{
    internal sealed class PresetsWindow : Window
    {
        private readonly Func<Window, string, Task>? _edit;
        private readonly List<(ComboBox Preset, TextBox Shortcut)> _entries = [];
        private readonly TextBlock _error = new() { Foreground = AppTheme.Error, TextWrapping = TextWrapping.Wrap };

        private readonly ListBox _fileList = new()
        {
            Name = "PresetFiles", MinHeight = 100, Background = AppTheme.SurfaceTint, BorderBrush = AppTheme.Outline,
            BorderThickness = new(1), CornerRadius = new(6)
        };

        private readonly List<string> _files;
        private readonly Grid _rows = new() { ColumnDefinitions = new("*,2*,Auto"), RowSpacing = 6 };

        public PresetsWindow(IReadOnlyList<string> source, IReadOnlyList<PresetHotkey>? hotkeys = null,
            Func<Window, string, Task>? edit = null, Func<IEnumerable<string>>? reservedHotkeys = null)
        {
            _files = source.ToList();
            _edit = edit;
            var reserved = reservedHotkeys ?? (() => []);
            this.Bind(TitleProperty, LiveText.Resource("PresetsTitle"));
            Width = 640;
            Height = 490;
            MinWidth = 480;
            MinHeight = 380;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = AppTheme.Surface;
            Foreground = AppTheme.Ink;
            _fileList.ItemTemplate = new FuncDataTemplate<string>((path, _) =>
            {
                var label = new TextBlock
                    { Text = Path.GetFileName(path), TextTrimming = TextTrimming.CharacterEllipsis };
                ToolTip.SetTip(label, path);
                return label;
            });
            var import = Button("ImportTemplate", "ImportPreset");
            import.Click += async (_, _) => await GuardAsync(ImportAsync);
            var create = Button("EditorNew", "CreatePreset");
            create.Click += async (_, _) => await GuardAsync(async () =>
            {
                var selected = await StorageProvider.SaveFilePickerAsync(new()
                {
                    Title = UiText.Get("EditorNew"), SuggestedFileName = "capture.rgpreset",
                    DefaultExtension = "rgpreset", FileTypeChoices = [TemplateType()], ShowOverwritePrompt = false,
                });
                await CreateTemplateAsync(selected?.TryGetLocalPath());
            });
            var editButton = Button("EditorEdit", "EditPreset");
            editButton.IsVisible = edit is not null;
            editButton.Click += async (_, _) => await GuardAsync(EditSelectedAsync);
            _fileList.DoubleTapped += async (_, _) => await GuardAsync(EditSelectedAsync);
            var remove = Button("RemoveTemplate", "RemovePreset");
            editButton.IsEnabled = remove.IsEnabled = _files.Count > 0;
            remove.Click += (_, _) =>
            {
                if (_fileList.SelectedItem is not string path) return;
                _files.Remove(path);
                RefreshFiles();
                RefreshBindings();
            };
            _fileList.SelectionChanged += (_, _) =>
            {
                editButton.IsEnabled = remove.IsEnabled = _fileList.SelectedItem is string;
            };
            var toolbar = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { import, create, editButton, remove },
            };
            foreach (var button in toolbar.Children) button.Margin = new(0, 0, 6, 4);
            var add = Button("AddPresetHotkey", "AddPresetHotkey");
            add.HorizontalAlignment = HorizontalAlignment.Left;
            add.Click += (_, _) => AddRow(null);
            _rows.RowDefinitions.Add(new(GridLength.Auto));
            _rows.Children.Add(new TextBlock
                { [!TextBlock.TextProperty] = LiveText.Resource("PresetName"), FontWeight = FontWeight.SemiBold });
            var shortcutHeading = new TextBlock
            {
                [!TextBlock.TextProperty] = LiveText.Resource("Shortcut"), FontWeight = FontWeight.SemiBold,
                Margin = new(6, 0)
            };
            Grid.SetColumn(shortcutHeading, 1);
            _rows.Children.Add(shortcutHeading);
            var bindings = new StackPanel
            {
                Spacing = 8, Children =
                {
                    _rows, add,
                    new TextBlock
                    {
                        [!TextBlock.TextProperty] = LiveText.Resource(HotkeyService.Supported
                            ? "HotkeyHint"
                            : "PlatformGlobalHotkeysUnavailable"),
                        TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = AppTheme.Muted,
                    },
                },
            };
            var save = Button("SaveSettings", "SavePresets");
            save.Classes.Add("primary");
            save.Click += (_, _) =>
            {
                try
                {
                    var names = Names();
                    var values = _entries.Select(e =>
                        new PresetHotkey(e.Preset.SelectedItem as string ?? "", e.Shortcut.Text ?? "")).ToList();
                    if (values.Any(b => !names.Contains(b.Preset) || string.IsNullOrWhiteSpace(b.Shortcut)))
                        throw new ArgumentException(UiText.Get("PresetHotkeyRequired"));
                    HotkeyService.Validate(reserved().Concat(values.Select(b => b.Shortcut)));
                    Close(new PresetConfiguration([.. _files], values));
                }
                catch (Exception exception)
                {
                    _error.Text = exception.Message;
                }
            };
            var cancel = Button("EditorCancel", "CancelPresets");
            cancel.Click += (_, _) => Close(null);
            var footer = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right,
                Children = { cancel, save },
            };
            var layout = new Grid
            {
                Margin = new(16), RowDefinitions = new("Auto,2*,Auto,3*,Auto,Auto"),
            };
            layout.Children.Add(toolbar);
            _fileList.Margin = new(0, 8, 0, 12);
            Grid.SetRow(_fileList, 1);
            layout.Children.Add(_fileList);
            var title = new TextBlock
            {
                [!TextBlock.TextProperty] = LiveText.Resource("PresetHotkeys"), FontWeight = FontWeight.SemiBold,
                Margin = new(0, 0, 0, 8),
            };
            Grid.SetRow(title, 2);
            layout.Children.Add(title);
            var scroll = new ScrollViewer { Content = bindings };
            Grid.SetRow(scroll, 3);
            layout.Children.Add(scroll);
            _error.Margin = new(0, 8, 0, 8);
            Grid.SetRow(_error, 4);
            layout.Children.Add(_error);
            Grid.SetRow(footer, 5);
            layout.Children.Add(footer);
            Content = layout;
            RefreshFiles();
            foreach (var binding in hotkeys ?? []) AddRow(binding);
        }

        private static Button Button(string label, string name)
        {
            return new()
            {
                [!ContentControl.ContentProperty] = LiveText.Resource(label), Name = name, Padding = new(9, 4),
                MinHeight = 28,
            };
        }

        private static FilePickerFileType TemplateType()
        {
            return new(UiText.Get("PresetTemplate"))
                { Patterns = ["*.rgpreset"] };
        }

        private string[] Names()
        {
            return PresetCatalog.Load(_files).Select(p => p.Name).ToArray();
        }

        private void RefreshFiles()
        {
            var selected = _fileList.SelectedItem;
            _fileList.ItemsSource = _files.ToArray();
            _fileList.SelectedItem = selected is string path && _files.Contains(path) ? path : _files.FirstOrDefault();
        }

        private void RefreshBindings()
        {
            try
            {
                var names = Names();
                foreach (var entry in _entries)
                {
                    var selected = entry.Preset.SelectedItem as string;
                    entry.Preset.ItemsSource = selected is not null && !names.Contains(selected)
                        ? names.Append(selected).ToArray()
                        : names;
                    entry.Preset.SelectedItem = selected ?? names.FirstOrDefault();
                }

                _error.Text = "";
            }
            catch (Exception exception)
            {
                _error.Text = exception.Message;
            }
        }

        internal async Task CreateTemplateAsync(string? path)
        {
            if (path is null) return;
            var name = Path.GetFileNameWithoutExtension(path);
            var names = Names();
            var candidate = name;
            for (var suffix = 2; names.Contains(candidate); suffix++) candidate = name + " " + suffix;
            var escaped = candidate.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
            var text = "preset \"" + escaped +
                       "\" {\n    area screen(0, 0, 1, 1);\n    script {\n        return;\n    }\n}\n";
            await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             4096, FileOptions.Asynchronous))
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                await writer.WriteAsync(text);
            _files.Add(Path.GetFullPath(path));
            RefreshFiles();
            _fileList.SelectedItem = Path.GetFullPath(path);
            RefreshBindings();
            await EditSelectedAsync();
        }

        private async Task ImportAsync()
        {
            var selected = await StorageProvider.OpenFilePickerAsync(new()
            {
                AllowMultiple = true, Title = UiText.Get("ImportTemplate"),
                FileTypeFilter =
                [
                    new(UiText.Get("PresetTemplate"))
                        { Patterns = ["*.rgpreset", "*.txt"] },
                ],
            });
            var files = _files.Concat(selected.Select(f => f.TryGetLocalPath()).OfType<string>())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            PresetCatalog.Load(files);
            _files.Clear();
            _files.AddRange(files);
            RefreshFiles();
            RefreshBindings();
        }

        private async Task EditSelectedAsync()
        {
            if (_edit is null || _fileList.SelectedItem is not string path) return;
            await _edit(this, path);
            RefreshBindings();
        }

        private async Task GuardAsync(Func<Task> action)
        {
            try
            {
                _error.Text = "";
                await action();
            }
            catch (Exception exception)
            {
                _error.Text = exception.Message;
            }
        }

        private void AddRow(PresetHotkey? binding)
        {
            string[] names;
            try
            {
                names = Names();
            }
            catch (Exception exception)
            {
                _error.Text = exception.Message;
                names = [];
            }

            var selectedName = binding?.Preset;
            if (selectedName is null && _fileList.SelectedItem is string path)
                try
                {
                    selectedName = PresetCatalog.Load([path]).FirstOrDefault()?.Name;
                }
                catch (Exception exception)
                {
                    _error.Text = exception.Message;
                }

            if (binding is not null && !names.Contains(binding.Preset)) names = [.. names, binding.Preset];
            var preset = new ComboBox
            {
                ItemsSource = names, SelectedItem = selectedName ?? names.FirstOrDefault(),
                HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 0,
            };
            preset.DropDownOpened += (_, _) => RefreshBindings();
            var shortcut = SettingsWindow.HotkeyBox(binding?.Shortcut ?? "");
            var clear = Button("Clear", "ClearPresetHotkey");
            clear.Click += (_, _) =>
            {
                shortcut.Text = "";
                shortcut.Focus();
            };
            var remove = Button("RemovePresetHotkey", "RemovePresetHotkey");
            var shortcutPanel = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new(6, 0) };
            shortcutPanel.Children.Add(shortcut);
            clear.Margin = new(4, 0, 0, 0);
            Grid.SetColumn(clear, 1);
            shortcutPanel.Children.Add(clear);
            var rowIndex = _rows.RowDefinitions.Count;
            _rows.RowDefinitions.Add(new(GridLength.Auto));
            Grid.SetRow(preset, rowIndex);
            Grid.SetRow(shortcutPanel, rowIndex);
            Grid.SetRow(remove, rowIndex);
            Grid.SetColumn(shortcutPanel, 1);
            Grid.SetColumn(remove, 2);
            _rows.Children.Add(preset);
            _rows.Children.Add(shortcutPanel);
            _rows.Children.Add(remove);
            _entries.Add((preset, shortcut));
            remove.Click += (_, _) =>
            {
                _entries.Remove((preset, shortcut));
                var removedRow = Grid.GetRow(preset);
                _rows.Children.Remove(preset);
                _rows.Children.Remove(shortcutPanel);
                _rows.Children.Remove(remove);
                _rows.RowDefinitions.RemoveAt(removedRow);
                foreach (var child in _rows.Children)
                {
                    var index = Grid.GetRow(child);
                    if (index > removedRow) Grid.SetRow(child, index - 1);
                }
            };
            if (binding is null) shortcut.Focus();
        }
    }
}

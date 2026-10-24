using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SettingsWindow : Window
    {
        private readonly TextBlock _error = new() { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap };

        public SettingsWindow(AppSettings settings, Func<Window, string, Task>? edit = null)
        {
            var draft = settings with
            {
                PresetHotkeys = [.. settings.PresetHotkeys], PresetFiles = [.. settings.PresetFiles],
            };
            Title = UiText.Get("SettingsTitle");
            Width = 590;
            Height = 430;
            MinWidth = 480;
            MinHeight = 300;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var general = Page();
            var recording = Page();
            var appearance = Page();
            var hotkeys = Page();
            var audio = Page();
            var cleanup = CleanupPage(draft, out var applyCleanup);
            var panel = general;
            var languageCodes = new[] { "system", "zh-Hans", "en" };
            var language = new ComboBox
            {
                ItemsSource = new[]
                    { UiText.Get("SystemLanguage"), UiText.Get("ChineseLanguage"), UiText.Get("EnglishLanguage") },
                SelectedIndex = Math.Max(0, Array.IndexOf(languageCodes, draft.Language)),
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            panel.Children.Add(Labeled(UiText.Get("Language"), language));
            var updates = new CheckBox
            {
                Name = "CheckForUpdates", Content = UiText.Get("AutomaticUpdates"),
                IsChecked = draft.CheckForUpdates,
            };
            general.Children.Add(updates);
            panel.Children.Add(new TextBlock
            {
                Text = UiText.Get("LanguageRestart"), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.SlateGray,
                FontSize = 12, Margin = new(104, -4, 0, 2),
            });
            var presets = new Button { Content = UiText.Get("PresetsTitle") };
            var captureSource = new ComboBox
            {
                ItemsSource = new[] { UiText.Get("SourceScreen"), UiText.Get("SourceWindow") },
                SelectedIndex = (int)draft.CaptureSource, HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            panel = recording;
            panel.Children.Add(Labeled(UiText.Get("CaptureSource"), captureSource));

            var quality = new ComboBox
            {
                ItemsSource = new[]
                    { UiText.Get("QualityFast"), UiText.Get("QualityBalanced"), UiText.Get("QualityBest") },
                SelectedIndex = (int)draft.Quality,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            panel.Children.Add(Labeled(UiText.Get("GifQuality"), quality));
            var ffmpeg = new CheckBox
            {
                Content = new TextBlock { Text = UiText.Get("UseFfmpeg"), TextWrapping = TextWrapping.Wrap },
                IsChecked = draft.UseFfmpeg,
            };
            panel.Children.Add(ffmpeg);
            panel.Children.Add(new TextBlock
            {
                Text = FfmpegEncoder.FindExecutable() is null ? UiText.Get("FfmpegMissing") : UiText.Get("FfmpegFound"),
                TextWrapping = TextWrapping.Wrap,
            });
            panel = appearance;
            var showBackground = new CheckBox
                { Content = UiText.Get("ShowBackground"), IsChecked = draft.ShowBackground };
            panel.Children.Add(showBackground);
            var background = new TextBox
                { Text = draft.BackgroundImage, Watermark = UiText.Get("OriginalBackground") };
            panel.Children.Add(Labeled(UiText.Get("BackgroundImage"), WithButton(background, UiText.Get("Browse"),
                async () =>
                {
                    var files = await StorageProvider.OpenFilePickerAsync(new()
                    {
                        Title = UiText.Get("ChooseBackground"),
                        FileTypeFilter =
                        [
                            new(UiText.Get("ImageFiles"))
                            {
                                Patterns =
                                [
                                    "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.webp", "*.gif", "*.ico", "*.tif", "*.tiff"
                                ]
                            },
                            new(UiText.Get("AllFiles")) { Patterns = ["*"] },
                        ],
                    });
                    if (files.Count > 0) background.Text = files[0].TryGetLocalPath();
                })));
            var resetBackground = new Button
                { Content = UiText.Get("ResetBackground"), Padding = new(6, 2), MinHeight = 0 };
            resetBackground.Click += (_, _) => background.Text = "";
            panel.Children.Add(Labeled("", resetBackground));
            var radius = new CompactNumberInput
                { Minimum = 0, Maximum = 50, Value = draft.SnapRadius, Width = 90, Height = 28 };
            var threshold = new CompactNumberInput
                { Minimum = 1, Maximum = 765, Value = draft.EdgeThreshold, Width = 90, Height = 28 };
            panel.Children.Add(Labeled(UiText.Get("SnapRadius"), radius));
            panel.Children.Add(Labeled(UiText.Get("EdgeThreshold"), threshold));
            var output = new TextBox { Text = draft.OutputDirectory, IsReadOnly = true };
            general.Children.Add(Labeled(UiText.Get("OutputDirectory"), WithButton(output, UiText.Get("Browse"),
                async () =>
                {
                    var folders =
                        await StorageProvider.OpenFolderPickerAsync(new() { Title = UiText.Get("ChooseOutput") });
                    if (folders.Count > 0) output.Text = folders[0].TryGetLocalPath();
                })));
            general.Children.Add(Labeled("", presets));
            panel = hotkeys;
            var record = HotkeyBox(draft.RecordHotkey);
            var select = HotkeyBox(draft.SelectHotkey);
            panel.Children.Add(Labeled(UiText.Get("RecordHotkey"), record));
            panel.Children.Add(Labeled(UiText.Get("SelectHotkey"), select));
            presets.Click += async (_, _) =>
            {
                try
                {
                    var value = await new PresetsWindow(draft.PresetFiles, draft.PresetHotkeys, edit,
                        () => [record.Text ?? "", select.Text ?? ""]).ShowDialog<PresetConfiguration?>(this);
                    if (value is not null)
                    {
                        draft.PresetFiles = value.Files;
                        draft.PresetHotkeys = value.Hotkeys;
                    }
                }
                catch (Exception exception)
                {
                    _error.Text = exception.Message;
                }
            };
            panel.Children.Add(new TextBlock
            {
                Text = UiText.Get(HotkeyService.Supported ? "HotkeyHint" : "PlatformGlobalHotkeysUnavailable"),
                TextWrapping = TextWrapping.Wrap,
            });
            panel = audio;
            var sound = new CheckBox { Content = UiText.Get("PlaySound"), IsChecked = draft.Sound };
            panel.Children.Add(sound);
            var start = new TextBox { Text = draft.StartSound };
            var stop = new TextBox { Text = draft.StopSound };
            panel.Children.Add(Labeled(UiText.Get("StartSound"), SoundPicker(start)));
            panel.Children.Add(Labeled(UiText.Get("StopSound"), SoundPicker(stop)));
            var import = new Button { Content = UiText.Get("ImportLegacySettings") };
            general.Children.Add(Labeled("", import));
            import.Click += async (_, _) =>
            {
                try
                {
                    var files = await StorageProvider.OpenFilePickerAsync(new()
                    {
                        Title = UiText.Get("ImportLegacySettings"),
                        FileTypeFilter = [new("XML") { Patterns = ["*.config", "*.xml"] }],
                    });
                    if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
                    var imported = LegacySettingsImporter.Import(path, draft);
                    draft.Fps = imported.Fps;
                    draft.Divisor = imported.Divisor;
                    draft.Format = imported.Format;
                    draft.Cursor = imported.Cursor;
                    draft.CheckForUpdates = imported.CheckForUpdates;
                    draft.UseFfmpeg = imported.UseFfmpeg;
                    draft.BackgroundImage = imported.BackgroundImage;
                    draft.ShowBackground = imported.ShowBackground;
                    draft.RecordHotkey = imported.RecordHotkey;
                    draft.SelectHotkey = imported.SelectHotkey;
                    draft.StartSound = imported.StartSound;
                    draft.StopSound = imported.StopSound;
                    draft.Sound = imported.Sound;
                    updates.IsChecked = draft.CheckForUpdates;
                    ffmpeg.IsChecked = draft.UseFfmpeg;
                    background.Text = draft.BackgroundImage;
                    showBackground.IsChecked = draft.ShowBackground;
                    record.Text = draft.RecordHotkey;
                    select.Text = draft.SelectHotkey;
                    start.Text = draft.StartSound;
                    stop.Text = draft.StopSound;
                    sound.IsChecked = draft.Sound;
                    _error.Text = "";
                }
                catch (Exception exception)
                {
                    _error.Text = exception.Message;
                }
            };

            var save = new Button
                { Content = UiText.Get("SaveSettings"), HorizontalAlignment = HorizontalAlignment.Right };
            save.Click += (_, _) =>
            {
                try
                {
                    HotkeyService.Validate([
                        record.Text ?? "", select.Text ?? "", .. draft.PresetHotkeys.Select(h => h.Shortcut),
                    ]);
                    if (string.IsNullOrWhiteSpace(output.Text))
                        throw new ArgumentException(UiText.Get("OutputRequired"));
                    draft.Quality = (GifQuality)quality.SelectedIndex;
                    draft.Language = languageCodes[Math.Max(0, language.SelectedIndex)];
                    draft.CheckForUpdates = updates.IsChecked == true;
                    draft.UseFfmpeg = ffmpeg.IsChecked == true;
                    draft.CaptureSource = (CaptureSourceKind)Math.Max(0, captureSource.SelectedIndex);
                    draft.OutputDirectory = output.Text;
                    draft.RecordHotkey = record.Text ?? "";
                    draft.SelectHotkey = select.Text ?? "";
                    draft.Sound = sound.IsChecked == true;
                    draft.StartSound = start.Text ?? "";
                    draft.StopSound = stop.Text ?? "";
                    draft.BackgroundImage = background.Text ?? "";
                    draft.ShowBackground = showBackground.IsChecked == true;
                    draft.SnapRadius = (int)(radius.Value ?? 12);
                    draft.EdgeThreshold = (int)(threshold.Value ?? 150);
                    applyCleanup();
                    draft.Save();
                    Close(draft);
                }
                catch (Exception ex)
                {
                    _error.Text = ex.Message;
                }
            };
            var pages = new (string Label, StackPanel Content)[]
            {
                (UiText.Get("GeneralHeading"), general), (UiText.Get("EncodingHeading"), recording),
                (UiText.Get("AppearanceHeading"), appearance), (UiText.Get("HotkeysHeading"), hotkeys),
                (UiText.Get("SoundHeading"), audio),
                (UiText.Get("CleanupHeading"), cleanup),
            };
            var navigation = new StackPanel { Spacing = 3, Margin = new(8, 12) };
            var view = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = general,
            };
            foreach (var page in pages)
            {
                var button = new Button { Content = page.Label, HorizontalAlignment = HorizontalAlignment.Stretch };
                button.Classes.Add("navigation");
                if (page.Content == general) button.Classes.Add("selected");
                button.Click += (_, _) =>
                {
                    foreach (var entry in navigation.Children.OfType<Button>()) entry.Classes.Remove("selected");
                    button.Classes.Add("selected");
                    view.Content = page.Content;
                    view.Offset = default;
                };
                navigation.Children.Add(button);
            }

            var body = new Grid { ColumnDefinitions = new("112,*") };
            body.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.Parse("#F3F5F7")),
                BorderBrush = new SolidColorBrush(Color.Parse("#E1E5EA")), BorderThickness = new(0, 0, 1, 0),
                Child = navigation,
            });
            Grid.SetColumn(view, 1);
            body.Children.Add(view);
            var footer = new Grid
                { ColumnDefinitions = new("*,Auto"), Margin = new(14, 9) };
            _error.Margin = new(0, 0, 12, 0);
            _error.VerticalAlignment = VerticalAlignment.Center;
            footer.Children.Add(_error);
            Grid.SetColumn(save, 1);
            footer.Children.Add(save);
            var layout = new Grid { RowDefinitions = new("*,Auto"), Background = Brushes.White };
            layout.Children.Add(body);
            var footerBorder = new Border
            {
                BorderBrush = new SolidColorBrush(Color.Parse("#E1E5EA")), BorderThickness = new(0, 1, 0, 0),
                Child = footer,
            };
            Grid.SetRow(footerBorder, 1);
            layout.Children.Add(footerBorder);
            Content = layout;
        }

        private static StackPanel Page()
        {
            return new() { Spacing = 10, Margin = new(16) };
        }

        private Control SoundPicker(TextBox box)
        {
            var panel = new Grid { ColumnDefinitions = new("*,Auto") };
            panel.Children.Add(WithButton(box, UiText.Get("BrowseSound"), async () =>
            {
                var files = await StorageProvider.OpenFilePickerAsync(new()
                {
                    Title = UiText.Get("ChooseSound"),
                    FileTypeFilter =
                        [new(UiText.Get("AudioFiles")) { Patterns = ["*.wav", "*.mp3", "*.ogg", "*.flac"] }],
                });
                if (files.Count > 0) box.Text = files[0].TryGetLocalPath();
            }));
            var clear = new Button { Content = UiText.Get("Clear"), Padding = new(6, 2), MinHeight = 0 };
            clear.Click += (_, _) => box.Text = "";
            clear.Margin = new(6, 0, 0, 0);
            Grid.SetColumn(clear, 1);
            panel.Children.Add(clear);
            return panel;
        }

        internal static TextBox HotkeyBox(string text)
        {
            var box = new TextBox
            {
                Text = text, IsReadOnly = true, IsEnabled = HotkeyService.Supported,
                Watermark = UiText.Get("EnterHotkey"),
            };
            ToolTip.SetTip(box, UiText.Get("HotkeyHint"));
            box.GotFocus += (_, _) => box.SelectAll();
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Tab && e.KeyModifiers == KeyModifiers.None) return;
                e.Handled = true;
                if (e.Key is Key.Back or Key.Delete && e.KeyModifiers == KeyModifiers.None)
                {
                    box.Text = "";
                    return;
                }

                if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt
                    or Key.RightAlt
                    or Key.LWin or Key.RWin) return;
                var prefix = "";
                if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) prefix += "Ctrl+";
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) prefix += "Shift+";
                if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) prefix += "Alt+";
                if (e.KeyModifiers.HasFlag(KeyModifiers.Meta)) prefix += "Win+";
                var key = e.Key.ToString();
                if (key.Length == 2 && key[0] == 'D' && char.IsAsciiDigit(key[1])) key = key[1..];
                try
                {
                    HotkeyService.Parse(prefix + key);
                    box.Text = prefix + key;
                }
                catch (ArgumentException)
                {
                }
            };
            return box;
        }

        private static Control Labeled(string text, Control control)
        {
            var row = new Grid { ColumnDefinitions = new("104,*") };
            row.Children.Add(new TextBlock
            {
                Text = text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
                Margin = new(0, 0, 10, 0),
            });
            control.VerticalAlignment = VerticalAlignment.Center;
            control.HorizontalAlignment =
                control is CompactNumberInput ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
            Grid.SetColumn(control, 1);
            row.Children.Add(control);
            return row;
        }

        private Control WithButton(Control control, string label, Func<Task> action)
        {
            var grid = new Grid { ColumnDefinitions = new("*,Auto") };
            var button = new Button { Content = label, Margin = new(8, 0, 0, 0) };
            button.Click += async (_, _) =>
            {
                try
                {
                    await action();
                }
                catch (Exception ex)
                {
                    _error.Text = ex.Message;
                }
            };
            Grid.SetColumn(button, 1);
            grid.Children.Add(control);
            grid.Children.Add(button);
            return grid;
        }
    }
}

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
        private TextBlock _error = new() { Foreground = AppTheme.Error, TextWrapping = TextWrapping.Wrap };

        public SettingsWindow(AppSettings settings)
        {
            Bind(TitleProperty, LiveText.Resource("SettingsTitle"));
            Classes.Add("settings-window");
            Width = 1120;
            Height = 800;
            MinWidth = 800;
            MinHeight = 540;
            Opened += (_, _) => FitSettingsToScreen();
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = AppTheme.Surface;
            Foreground = AppTheme.Ink;
            Closed += (_, _) => { DisposeContent(); };
            BuildContent(CloneDraft(settings), "GeneralHeading");
        }

        internal Action<AppSettings>? ApplySettings { private get; init; }

        private void BuildContent(AppSettings draft, string selectedPage)
        {
            DisposeContent();
            _contentLifetime = new();
            try
            {
                BuildContentCore(draft, selectedPage);
            }
            catch
            {
                DisposeContent();
                throw;
            }
        }

        private void BuildContentCore(AppSettings draft, string selectedPage)
        {
            var contentToken = ContentCancellationToken;
            _draft = draft;
            _currentPage = selectedPage;
            _error = new() { Foreground = AppTheme.Error, TextWrapping = TextWrapping.Wrap };
            var recording = Page();
            var screenshotSave = ScreenshotSavePage(draft, out var applyScreenshotSave);
            var clipboard = ScreenshotClipboardPage(draft, out var applyScreenshotClipboard,
                out var captureClipboardOptions);
            var screenshots = CapturePreferencesPage(draft, captureClipboardOptions,
                out var applyCapturePreferences);
            var general = BuildSystemPage(draft, clipboard);
            var annotation = AnnotationPage(draft, out var applyAnnotation);
            var pins = PinsPage(draft.PinnedImages, out var readPins);
            var appearance = BuildAppearancePage(draft);
            var storage = Page();
            var audio = Page();
            var selectionShortcuts = SelectionShortcutsPage(draft);
            var mouseShortcuts = MouseShortcutsPage(draft);
            var globalMouse = GlobalMousePage(draft);
            var cleanup = CleanupPage(draft, out var applyCleanup);
            var restorePins = new CheckBox
            {
                [!ContentProperty] = LiveText.Resource("RestorePinnedImages"),
                IsChecked = draft.RestorePinnedImages,
            };
            pins.Children.Insert(0, restorePins);
            var panel = recording;
            var colorSettings = Page();
            var captureColor = ColorModeBox(draft.CaptureColorMode);
            colorSettings.Children.Add(Labeled("OutputColorMode", captureColor));
            var captureToneMap = ToneMapBox(draft.CaptureToneMap);
            colorSettings.Children.Add(Labeled("HdrToneMap", captureToneMap));
            var quality = new ComboBox
            {
                ItemsSource = new[]
                    { UiText.Get("QualityFast"), UiText.Get("QualityBalanced"), UiText.Get("QualityBest") },
                SelectedIndex = (int)draft.Quality,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            panel.Children.Add(Labeled("GifQuality", quality));

            SubscribeContentLanguage(RefreshLanguage);
            var ffmpeg = new CheckBox
            {
                Content = new TextBlock
                    { [!TextBlock.TextProperty] = LiveText.Resource("UseFfmpeg"), TextWrapping = TextWrapping.Wrap },
                IsChecked = draft.UseFfmpeg,
            };
            panel.Children.Add(new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    ffmpeg,
                    new TextBlock
                    {
                        [!TextBlock.TextProperty] = LiveText.Resource(FfmpegEncoder.FindExecutable() is null
                            ? "FfmpegMissing"
                            : "FfmpegFound"),
                        TextWrapping = TextWrapping.Wrap, Foreground = AppTheme.Muted,
                        FontSize = 12, Margin = new(24, 0, 0, 0),
                    },
                },
            });
            panel = appearance;
            var showBackground = new CheckBox
            {
                [!ContentProperty] = LiveText.Resource("ShowBackground"),
                IsChecked = draft.ShowBackground,
            };
            panel.Children.Add(showBackground);
            var background = new TextBox
            {
                Text = draft.BackgroundImage, [!TextBox.WatermarkProperty] = LiveText.Resource("OriginalBackground"),
            };
            panel.Children.Add(Labeled("BackgroundImage", WithButton(background, "Browse",
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
                                    "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.webp", "*.gif", "*.ico", "*.tif", "*.tiff",
                                ],
                            },
                            new(UiText.Get("AllFiles")) { Patterns = ["*"] },
                        ],
                    });
                    if (files.Count > 0) background.Text = files[0].TryGetLocalPath();
                })));
            var resetBackground = new Button
            {
                [!ContentProperty] = LiveText.Resource("ResetBackground"), Padding = new(6, 2),
                HorizontalAlignment = HorizontalAlignment.Left,
                MinHeight = 0,
            };
            resetBackground.Click += (_, _) => background.Text = "";
            panel.Children.Add(resetBackground);
            var output = new TextBox { Text = draft.OutputDirectory, IsReadOnly = true };
            storage.Children.Add(Labeled("OutputDirectory", WithButton(output, "Browse",
                async () =>
                {
                    var folders =
                        await StorageProvider.OpenFolderPickerAsync(new() { Title = UiText.Get("ChooseOutput") });
                    if (folders.Count > 0) output.Text = folders[0].TryGetLocalPath();
                })));
            var openOutput = new Button
            {
                Name = "OpenRecordingDirectory",
                [!ContentProperty] = LiveText.Resource("OpenRecordingDirectory"),
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            openOutput.Click += async (_, _) =>
            {
                var path = output.Text;
                openOutput.IsEnabled = false;
                try
                {
                    if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException(UiText.Get("OutputRequired"));
                    await Task.Run(() => PlatformShell.OpenDirectory(path), contentToken);
                    contentToken.ThrowIfCancellationRequested();
                    _error.Text = "";
                }
                catch (OperationCanceledException) when (contentToken.IsCancellationRequested)
                {
                }
                catch (Exception exception)
                {
                    if (!contentToken.IsCancellationRequested) _error.Text = exception.Message;
                }
                finally
                {
                    if (!contentToken.IsCancellationRequested) openOutput.IsEnabled = true;
                }
            };
            storage.Children.Add(openOutput);
            WrapSettingsFields(storage);
            storage.Children.Add(screenshotSave);
            storage.Children.Add(SettingsSection("CleanupHeading", cleanup));
            var actions = ActionsPage(draft);
            panel = audio;
            var sound = new CheckBox
                { [!ContentProperty] = LiveText.Resource("PlaySound"), IsChecked = draft.Sound };
            panel.Children.Add(sound);
            var start = new TextBox { Text = draft.StartSound };
            var stop = new TextBox { Text = draft.StopSound };
            panel.Children.Add(Labeled("StartSound", SoundPicker(start)));
            panel.Children.Add(Labeled("StopSound", SoundPicker(stop)));
            screenshots.Children.Add(SettingsSection("SettingsColorHeading", colorSettings));
            screenshots.Children.Add(SettingsSection("EncodingHeading", recording));
            var transfer = TransferPage(() =>
            {
                ApplyLiveSettings();
                return CloneDraft(_appliedSettings ?? draft);
            });
            general.Children.Add(SettingsSection("SoundHeading", audio));
            WrapSettingsFields(pins);
            var resetAll = new Button { Name = "ResetAllSettings", Foreground = AppTheme.Error };
            resetAll.Bind(ContentProperty, LiveText.Resource("SettingsResetAll"));
            resetAll.Bind(ToolTip.TipProperty, LiveText.Resource("SettingsResetAllHint"));
            resetAll.Click += (_, _) => ResetAllSettings();
            var resetCurrent = new Button { Name = "ResetCurrentSettings" };
            resetCurrent.Bind(ContentProperty, LiveText.Resource("SettingsResetCurrent"));
            resetCurrent.Click += (_, _) => ResetCurrentSettings();
            var body = SettingsBody([
                ("GeneralHeading", general),
                ("AppearanceHeading", appearance),
                ("ScreenshotHeading", screenshots),
                ("SettingsAnnotationHeading", annotation),
                ("SettingsPinsHeading", pins),
                ("ScreenshotSaveHeading", storage),
                ("GlobalActionsHeading", actions),
                ("SettingsLocalInput", selectionShortcuts),
                ("MouseShortcutsHeading", mouseShortcuts),
                ("GlobalMouseHeading", globalMouse),
                ("SettingsTransferHeading", transfer),
                ("AboutTitle", AboutWindow.CreateContent(draft)),
            ], selectedPage, page =>
            {
                _currentPage = page;
                resetCurrent.IsEnabled = SettingsDefaults.CanResetPage(page);
                resetCurrent.Bind(ToolTip.TipProperty, LiveText.Resource(page == "GlobalActionsHeading"
                    ? "SettingsResetActionsHint"
                    : "SettingsResetCurrentHint"));
            }, resetAll);
            var footer = new Grid
                { ColumnDefinitions = new("*,Auto"), Margin = new(14, 9) };
            _error.Margin = new(0, 0, 12, 0);
            _error.VerticalAlignment = VerticalAlignment.Center;
            footer.Children.Add(new ScrollViewer
            {
                Content = _error, MaxHeight = 72,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            });
            var close = new Button { Name = "CloseSettings", [!ContentProperty] = LiveText.Resource("Close") };
            close.Click += (_, _) => Close();
            var footerActions = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { resetCurrent, close },
            };
            Grid.SetColumn(footerActions, 1);
            footer.Children.Add(footerActions);
            var layout = new Grid { RowDefinitions = new("*,Auto"), Background = AppTheme.Surface };
            layout.Children.Add(body);
            var footerBorder = new Border
            {
                BorderBrush = AppTheme.Outline, BorderThickness = new(0, 1, 0, 0),
                Background = AppTheme.SurfaceTint, Child = footer,
            };
            Grid.SetRow(footerBorder, 1);
            layout.Children.Add(footerBorder);
            Content = layout;
            PreviewAppearance(draft.Appearance);
            _readDraft = ReadDraft;
            _appliedSettings = CloneDraft(draft);
            SubscribeLiveSettings();
            return;

            void ReadDraft()
            {
                contentToken.ThrowIfCancellationRequested();
                draft.Quality = (GifQuality)quality.SelectedIndex;
                draft.CaptureColorMode = (OutputColorMode)Math.Max(0, captureColor.SelectedIndex);
                draft.CaptureToneMap = (HdrToneMapMode)Math.Max(0, captureToneMap.SelectedIndex);
                draft.RestorePinnedImages = restorePins.IsChecked == true;
                draft.UseFfmpeg = ffmpeg.IsChecked == true;
                draft.OutputDirectory = output.Text ?? "";
                draft.Sound = sound.IsChecked == true;
                draft.StartSound = start.Text ?? "";
                draft.StopSound = stop.Text ?? "";
                draft.BackgroundImage = background.Text ?? "";
                draft.ShowBackground = showBackground.IsChecked == true;
                _liveReadErrors.Clear();
                ReadLiveSection(applyCapturePreferences);
                ReadLiveSection(applyCleanup);
                ReadLiveSection(applyScreenshotClipboard);
                ReadLiveSection(applyScreenshotSave);
                ReadLiveSection(applyAnnotation);
                ReadLiveSection(() => draft.PinnedImages = readPins());
            }

            void RefreshLanguage()
            {
                var qualityIndex = quality.SelectedIndex;
                var colorIndex = captureColor.SelectedIndex;
                var toneMapIndex = captureToneMap.SelectedIndex;
                captureToneMap.ItemsSource = ToneMapLabels();
                captureToneMap.SelectedIndex = toneMapIndex;
                captureColor.ItemsSource = ColorModeLabels();
                captureColor.SelectedIndex = colorIndex;
                quality.ItemsSource = new[]
                {
                    UiText.Get("QualityFast"), UiText.Get("QualityBalanced"), UiText.Get("QualityBest"),
                };
                quality.SelectedIndex = qualityIndex;
            }
        }

        private static StackPanel Page()
        {
            return new() { Spacing = 12, Margin = new(16) };
        }

        private static ComboBox ColorModeBox(OutputColorMode mode)
        {
            return new()
            {
                ItemsSource = ColorModeLabels(), SelectedIndex = (int)mode,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
        }

        private static string[] ColorModeLabels()
        {
            return [UiText.Get("ColorModeSdr"), UiText.Get("ColorModeHdr"), UiText.Get("ColorModeBoth")];
        }

        private static ComboBox ToneMapBox(HdrToneMapMode mode)
        {
            return new()
            {
                ItemsSource = ToneMapLabels(), SelectedIndex = (int)mode,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
        }

        private static string[] ToneMapLabels()
        {
            return [UiText.Get("HdrToneMapHighlight"), UiText.Get("HdrToneMapReference")];
        }

        private Grid SoundPicker(TextBox box)
        {
            var panel = new Grid { ColumnDefinitions = new("*,Auto") };
            panel.Children.Add(WithButton(box, "BrowseSound", async () =>
            {
                var files = await StorageProvider.OpenFilePickerAsync(new()
                {
                    Title = UiText.Get("ChooseSound"),
                    FileTypeFilter =
                        [new(UiText.Get("AudioFiles")) { Patterns = ["*.wav", "*.mp3", "*.ogg", "*.flac"] }],
                });
                if (files.Count > 0) box.Text = files[0].TryGetLocalPath();
            }));
            var clear = new Button
                { [!ContentProperty] = LiveText.Resource("Clear"), Padding = new(6, 2), MinHeight = 0 };
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
                Text = KeyGestureDisplay.Format(text), IsReadOnly = true, IsEnabled = HotkeyService.Supported,
                [!TextBox.WatermarkProperty] = LiveText.Resource("EnterHotkey"),
            };
            box.Bind(ToolTip.TipProperty, LiveText.Resource("HotkeyHint"));
            GlobalInputCapture.Observe(box);
            box.GotFocus += (_, _) => box.SelectAll();
            box.KeyDown += (_, e) =>
            {
                if (e is { Key: Key.Tab, KeyModifiers: KeyModifiers.None or KeyModifiers.Shift }) return;
                e.Handled = true;
                switch (e.Key)
                {
                    case Key.Back or Key.Delete when e.KeyModifiers == KeyModifiers.None:
                        box.Text = "";
                        return;
                    case Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt
                        or Key.RightAlt or Key.LWin or Key.RWin:
                        return;
                }

                var prefix = "";
                if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) prefix += "Ctrl+";
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) prefix += "Shift+";
                if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) prefix += "Alt+";
                if (e.KeyModifiers.HasFlag(KeyModifiers.Meta)) prefix += "Win+";
                var key = KeyGestureDisplay.Format(new KeyGesture(e.Key));
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

        private static Grid Labeled(string text, Control control)
        {
            var row = new Grid
            {
                ColumnDefinitions = new("*,*"), ColumnSpacing = 16, MinHeight = 38,
                Margin = new(0, 2),
            };
            var label = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
            };
            if (text.Length > 0) label.Bind(TextBlock.TextProperty, LiveText.Resource(text));
            row.Children.Add(label);
            control.VerticalAlignment = VerticalAlignment.Center;
            control.HorizontalAlignment =
                control is CompactNumberInput ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
            Grid.SetColumn(control, 1);
            row.Children.Add(control);
            return row;
        }

        private Grid WithButton(Control control, string label, Func<Task> action)
        {
            var token = ContentCancellationToken;
            var error = _error;
            var grid = new Grid { ColumnDefinitions = new("*,Auto") };
            var button = new Button
                { [!ContentProperty] = LiveText.Resource(label), Margin = new(8, 0, 0, 0) };
            button.Click += async (_, _) =>
            {
                try
                {
                    token.ThrowIfCancellationRequested();
                    await action();
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                }
                catch (Exception ex)
                {
                    if (!token.IsCancellationRequested) error.Text = ex.Message;
                }
            };
            Grid.SetColumn(button, 1);
            grid.Children.Add(control);
            grid.Children.Add(button);
            return grid;
        }
    }
}

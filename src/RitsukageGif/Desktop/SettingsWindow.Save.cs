using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SettingsWindow
    {
        private StackPanel ScreenshotSavePage(AppSettings draft, out Action apply)
        {
            var page = Page();
            var value = draft.ScreenshotSave;
            var quality = new CompactNumberInput
                { Name = "ScreenshotSaveQuality", Minimum = 0, Maximum = 100, Value = value.Quality, Width = 100 };
            var after = SaveActionsEditor(value.AfterSave, out var readActions);
            page.Children.Add(SettingRow("ScreenshotSaveQuality", quality,
                static settings => settings.ScreenshotSave.Quality));
            page.Children.Add(SettingRow("ScreenshotAfterManualQuickSave", after,
                static settings => settings.ScreenshotSave.AfterSave));
            var manual = SaveProfileEditor(value, draft.OutputDirectory, out var readManual,
                static settings => settings.ScreenshotSave, true);
            var remember = new CheckBox
            {
                Name = "ScreenshotRememberExtension", IsChecked = value.RememberExtension,
                [!ContentProperty] = LiveText.Resource("ScreenshotRememberExtension"),
            };
            manual.Children.Add(Setting(remember, static settings => settings.ScreenshotSave.RememberExtension));
            page.Children.Add(SettingsSection("ScreenshotManualSave", manual));
            var notificationAction = new ComboBox
            {
                Name = "ScreenshotQuickNotificationAction", SelectedIndex = (int)value.QuickNotificationAction,
            };
            RefreshNotificationActions();
            SubscribeContentLanguage(RefreshNotificationActions);
            page.Children.Add(SettingsSection("ScreenshotQuickSave",
                SaveProfileEditor(value.Quick, draft.OutputDirectory, out var readQuick,
                    static settings => settings.ScreenshotSave.Quick, notificationAction: notificationAction)));
            var automatic = SaveProfileEditor(value.Automatic, draft.OutputDirectory, out var readAutomatic,
                static settings => settings.ScreenshotSave.Automatic);
            var enabled = new CheckBox
            {
                Name = "ScreenshotAutoSaveEnabled", IsChecked = value.AutoSaveEnabled,
                [!ContentProperty] = LiveText.Resource("ScreenshotAutomaticSave"),
            };
            automatic.IsEnabled = value.AutoSaveEnabled;
            enabled.IsCheckedChanged += (_, _) => automatic.IsEnabled = enabled.IsChecked == true;
            var triggers = SaveTriggersEditor(value.AutoSaveTriggers, out var readTriggers);
            var cleanup = new CheckBox
            {
                Name = "ScreenshotAutomaticCleanup", IsChecked = value.AutomaticCleanupMaxAgeDays is not null,
                [!ContentProperty] = LiveText.Resource("ScreenshotAutomaticCleanup"),
            };
            var days = new CompactNumberInput
            {
                Name = "ScreenshotAutomaticCleanupDays", Minimum = 1, Maximum = 36500,
                Value = value.AutomaticCleanupMaxAgeDays ?? 7, Width = 90,
                IsEnabled = cleanup.IsChecked == true,
            };
            cleanup.IsCheckedChanged += (_, _) => days.IsEnabled = cleanup.IsChecked == true;
            var cleanupRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            cleanupRow.Children.Add(cleanup);
            cleanupRow.Children.Add(days);
            cleanupRow.Children.Add(new TextBlock
            {
                [!TextBlock.TextProperty] = LiveText.Resource("ScreenshotCleanupDays"),
                VerticalAlignment = VerticalAlignment.Center,
            });
            automatic.Children.Add(SettingRow("ScreenshotAutoTriggers", triggers,
                static settings => settings.ScreenshotSave.AutoSaveTriggers));
            automatic.Children.Add(cleanupRow);
            var automaticSection = SettingsSection("ScreenshotAutomaticSave", automatic);
            automaticSection.Children[0] = enabled;
            page.Children.Add(automaticSection);
            apply = () =>
            {
                var current = readManual();
                draft.ScreenshotSave = value with
                {
                    Directory = current.Directory, Format = current.Format, Quality = (int)ReadSettingsNumber(quality),
                    OutputSize = current.OutputSize,
                    NameTemplate = current.NameTemplate, ConflictPolicy = current.ConflictPolicy,
                    AfterSave = readActions(), Notify = current.Notify,
                    RememberExtension = remember.IsChecked == true,
                    Quick = readQuick(), Automatic = readAutomatic(), AutoSaveTriggers = readTriggers(),
                    AutoSaveEnabled = enabled.IsChecked == true,
                    AutomaticCleanupMaxAgeDays = cleanup.IsChecked == true ? (int)ReadSettingsNumber(days) : null,
                    QuickNotificationAction = (SaveNotificationAction)Math.Max(0, notificationAction.SelectedIndex),
                };
            };
            return page;

            void RefreshNotificationActions()
            {
                var selected = notificationAction.SelectedIndex;
                notificationAction.ItemsSource = new[]
                {
                    UiText.Get("ScreenshotAfterOpenFolder"), UiText.Get("ScreenshotAfterCopyFile"),
                    UiText.Get("ScreenshotAfterCopyPath"),
                };
                notificationAction.SelectedIndex = selected;
            }
        }

        private StackPanel SaveProfileEditor(ScreenshotSaveProfile value, string fallbackDirectory,
            out Func<ScreenshotSaveProfile> read, Func<AppSettings, ScreenshotSaveProfile> profileGetter,
            bool manual = false, ComboBox? notificationAction = null)
        {
            var token = ContentCancellationToken;
            var page = Page();
            var format = new ComboBox
            {
                ItemsSource = new[] { ".png", ".jpg", ".bmp", ".webp", ".avif" },
                SelectedIndex = (int)value.Format, MinWidth = 100,
            };
            var name = new TextBox
            {
                Text = manual
                    ? value.NameTemplate
                    : Path.Combine(string.IsNullOrWhiteSpace(value.Directory) ? fallbackDirectory : value.Directory,
                        value.NameTemplate),
            };
            ToolTip.SetTip(name, UiText.Get("ScreenshotNameTemplateHint"));
            var conflict = new ComboBox
                { ItemsSource = ScreenshotConflictLabels(), SelectedIndex = (int)value.ConflictPolicy };
            var notify = new CheckBox
                { IsChecked = value.Notify, [!ContentProperty] = LiveText.Resource("ScreenshotSaveNotify") };
            var preview = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = AppTheme.Muted };
            var sample = new ScreenshotNameContext(DateTime.Now);
            var browse = new Button { [!ContentProperty] = LiveText.Resource("Browse") };
            var open = new Button { [!ContentProperty] = LiveText.Resource("OpenFolder") };
            var pathRow = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 4 };
            pathRow.Children.Add(name);
            Grid.SetColumn(format, 1);
            pathRow.Children.Add(format);
            browse.Click += async (_, _) =>
            {
                try
                {
                    var folders = await StorageProvider.OpenFolderPickerAsync(new()
                    {
                        Title = UiText.Get("ChooseOutput"), AllowMultiple = false,
                        SuggestedStartLocation =
                            await StorageProvider.TryGetFolderFromPathAsync(Read().ResolveDirectory(fallbackDirectory)),
                    });
                    if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } selected)
                        name.Text = Path.Combine(selected, Read().NameTemplate);
                    _error.Text = "";
                }
                catch (Exception error)
                {
                    _error.Text = error.Message;
                }
            };
            open.Click += async (_, _) =>
            {
                open.IsEnabled = false;
                try
                {
                    var path = Read().ResolveDirectory(fallbackDirectory);
                    await Task.Run(() => PlatformShell.OpenDirectory(path), token);
                    token.ThrowIfCancellationRequested();
                    _error.Text = "";
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                }
                catch (Exception error)
                {
                    if (!token.IsCancellationRequested) _error.Text = error.Message;
                }
                finally
                {
                    if (!token.IsCancellationRequested) open.IsEnabled = true;
                }
            };
            if (notificationAction is not null)
            {
                notificationAction.IsEnabled = value.Notify;
                notify.IsCheckedChanged += (_, _) => notificationAction.IsEnabled = notify.IsChecked == true;
                var notificationRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                notificationRow.Children.Add(notify);
                notificationRow.Children.Add(notificationAction);
                page.Children.Add(notificationRow);
            }

            page.Children.Add(SettingRow(manual ? "ScreenshotNameTemplate" : "OutputDirectory", pathRow,
                settings => profileGetter(settings).NameTemplate));
            page.Children.Add(Labeled("ScreenshotNamePreview", preview));
            if (!manual)
            {
                var destinations = new Grid { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = 6 };
                destinations.Children.Add(conflict);
                Grid.SetColumn(open, 1);
                destinations.Children.Add(open);
                Grid.SetColumn(browse, 2);
                destinations.Children.Add(browse);
                page.Children.Add(SettingRow("ScreenshotConflictPolicy", destinations,
                    settings => profileGetter(settings).ConflictPolicy));
            }

            name.TextChanged += (_, _) => Preview();
            format.SelectionChanged += (_, _) => Preview();
            Preview();
            SubscribeContentLanguage(RefreshLanguage);
            read = Read;
            return page;

            void Preview()
            {
                try
                {
                    var profile = Read();
                    preview.Text = manual
                        ? profile.CreateFileName(sample)
                        : profile.CreatePath(fallbackDirectory, sample);
                }
                catch (Exception error) when (error is ArgumentException or NotSupportedException
                                                  or PathTooLongException)
                {
                    preview.Text = error.Message;
                }
            }

            ScreenshotSaveProfile Read()
            {
                var text = name.Text ?? "";
                return value with
                {
                    Directory = manual ? value.Directory : Path.GetDirectoryName(text) ?? "",
                    Format = (StaticImageFormat)Math.Max(0, format.SelectedIndex),
                    NameTemplate = manual ? text : Path.GetFileName(text),
                    ConflictPolicy = (ScreenshotConflictPolicy)Math.Max(0, conflict.SelectedIndex),
                    AfterSave = value.AfterSave,
                    Notify = notify.IsChecked == true,
                };
            }

            void RefreshLanguage()
            {
                var selected = conflict.SelectedIndex;
                conflict.ItemsSource = ScreenshotConflictLabels();
                conflict.SelectedIndex = selected;
            }
        }

        private static string[] ScreenshotConflictLabels()
        {
            return
            [
                UiText.Get("ScreenshotConflictRename"), UiText.Get("ScreenshotConflictOverwrite"),
                UiText.Get("ScreenshotConflictFail"),
            ];
        }

        private Button SaveTriggersEditor(ScreenshotAutoSaveTriggers value, out Func<ScreenshotAutoSaveTriggers> read)
        {
            var choices = new StackPanel { Spacing = 4 };
            var summary = new TextBlock
            {
                TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
            };
            var button = new Button
            {
                Name = "ScreenshotAutoSaveTriggers", HorizontalContentAlignment = HorizontalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch, Flyout = new Flyout { Content = choices },
            };
            var content = new Grid { ColumnDefinitions = new("*,16"), ColumnSpacing = 8 };
            content.Children.Add(summary);
            var arrow = DropDownChevron.Create(button);
            Grid.SetColumn(arrow, 1);
            content.Children.Add(arrow);
            button.Content = content;
            var triggers = new[]
            {
                ScreenshotAutoSaveTriggers.Copy, ScreenshotAutoSaveTriggers.Pin,
                ScreenshotAutoSaveTriggers.Recognize, ScreenshotAutoSaveTriggers.Translate,
                ScreenshotAutoSaveTriggers.Save, ScreenshotAutoSaveTriggers.Scrolling,
                ScreenshotAutoSaveTriggers.Screenshot,
            };
            foreach (var trigger in triggers)
            {
                var choice = new CheckBox
                {
                    [!ContentProperty] = LiveText.Resource("ScreenshotAuto" + trigger),
                    IsChecked = value.HasFlag(trigger),
                };
                choice.IsCheckedChanged += (_, _) =>
                {
                    value = choice.IsChecked == true ? value | trigger : value & ~trigger;
                    Refresh();
                    RequestLiveApply();
                };
                choices.Children.Add(choice);
            }

            SubscribeContentLanguage(Refresh);
            Refresh();
            read = () => value;
            return button;

            void Refresh()
            {
                summary.Text = value == ScreenshotAutoSaveTriggers.None
                    ? UiText.Get("ScreenshotAfterNone")
                    : string.Join("; ", triggers.Where(trigger => value.HasFlag(trigger))
                        .Select(trigger => UiText.Get("ScreenshotAuto" + trigger)));
                ToolTip.SetTip(button, summary.Text);
            }
        }

        private Button SaveActionsEditor(ScreenshotAfterSaveAction value, out Func<ScreenshotAfterSaveAction> read)
        {
            var file = new CheckBox
            {
                [!ContentProperty] = LiveText.Resource("ScreenshotAfterCopyFile"),
                IsChecked = value is ScreenshotAfterSaveAction.CopyFile or ScreenshotAfterSaveAction.CopyFileAndPath,
            };
            var path = new CheckBox
            {
                [!ContentProperty] = LiveText.Resource("ScreenshotAfterCopyPath"),
                IsChecked = value is ScreenshotAfterSaveAction.CopyPath or ScreenshotAfterSaveAction.CopyFileAndPath,
            };
            var options = new StackPanel { Spacing = 4 };
            options.Children.Add(file);
            options.Children.Add(path);
            var button = new Button
            {
                Name = "ScreenshotSaveActions", HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch, Flyout = new Flyout { Content = options },
            };
            file.IsCheckedChanged += (_, _) => Changed();
            path.IsCheckedChanged += (_, _) => Changed();
            SubscribeContentLanguage(Refresh);
            Refresh();
            read = () => value;
            return button;

            void Changed()
            {
                value = (file.IsChecked == true, path.IsChecked == true) switch
                {
                    (true, true) => ScreenshotAfterSaveAction.CopyFileAndPath,
                    (true, false) => ScreenshotAfterSaveAction.CopyFile,
                    (false, true) => ScreenshotAfterSaveAction.CopyPath,
                    _ => ScreenshotAfterSaveAction.None,
                };
                Refresh();
                RequestLiveApply();
            }

            void Refresh()
            {
                var text = value switch
                {
                    ScreenshotAfterSaveAction.CopyFileAndPath => UiText.Get("ScreenshotAfterCopyFile") + "; " +
                                                                 UiText.Get("ScreenshotAfterCopyPath"),
                    ScreenshotAfterSaveAction.CopyFile => UiText.Get("ScreenshotAfterCopyFile"),
                    ScreenshotAfterSaveAction.CopyPath => UiText.Get("ScreenshotAfterCopyPath"),
                    ScreenshotAfterSaveAction.CopyImage => UiText.Get("ScreenshotAfterCopyImage"),
                    ScreenshotAfterSaveAction.OpenFolder => UiText.Get("ScreenshotAfterOpenFolder"),
                    _ => UiText.Get("ScreenshotAfterNone"),
                };
                var content = new Grid { ColumnDefinitions = new("*,16"), ColumnSpacing = 8 };
                content.Children.Add(new TextBlock
                {
                    Text = text, TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center,
                });
                var arrow = DropDownChevron.Create(button);
                Grid.SetColumn(arrow, 1);
                content.Children.Add(arrow);
                button.Content = content;
                ToolTip.SetTip(button, text);
            }
        }
    }
}

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
            var manual = SaveProfileEditor(value, draft.OutputDirectory, out var readManual,
                static settings => settings.ScreenshotSave);
            var remember = new CheckBox
            {
                Name = "ScreenshotRememberExtension", IsChecked = value.RememberExtension,
                [!ContentProperty] = LiveText.Resource("ScreenshotRememberExtension"),
            };
            manual.Children.Add(Setting(remember, static settings => settings.ScreenshotSave.RememberExtension));
            page.Children.Add(SettingsSection("ScreenshotManualSave", manual));
            page.Children.Add(SettingsSection("ScreenshotQuickSave",
                SaveProfileEditor(value.Quick, draft.OutputDirectory, out var readQuick,
                    static settings => settings.ScreenshotSave.Quick)));
            var automatic = SaveProfileEditor(value.Automatic, draft.OutputDirectory, out var readAutomatic,
                static settings => settings.ScreenshotSave.Automatic);
            var triggers = new WrapPanel { Orientation = Orientation.Horizontal };
            var checks = new Dictionary<ScreenshotAutoSaveTriggers, CheckBox>();
            foreach (var trigger in new[]
                     {
                         ScreenshotAutoSaveTriggers.Copy, ScreenshotAutoSaveTriggers.Pin,
                         ScreenshotAutoSaveTriggers.Recognize, ScreenshotAutoSaveTriggers.Save,
                         ScreenshotAutoSaveTriggers.Scrolling,
                     })
            {
                var check = new CheckBox
                {
                    Margin = new(0, 0, 12, 6), IsChecked = value.AutoSaveTriggers.HasFlag(trigger),
                    [!ContentProperty] = LiveText.Resource("ScreenshotAuto" + trigger),
                };
                checks.Add(trigger, check);
                triggers.Children.Add(check);
            }

            automatic.Children.Insert(0,
                SettingRow("ScreenshotAutoTriggers", triggers,
                    static settings => settings.ScreenshotSave.AutoSaveTriggers));
            page.Children.Add(SettingsSection("ScreenshotAutomaticSave", automatic));
            apply = () =>
            {
                var current = readManual();
                var flags = checks.Where(pair => pair.Value.IsChecked == true)
                    .Aggregate(ScreenshotAutoSaveTriggers.None, (result, pair) => result | pair.Key);
                draft.ScreenshotSave = value with
                {
                    Directory = current.Directory, Format = current.Format, Quality = current.Quality,
                    OutputSize = current.OutputSize,
                    NameTemplate = current.NameTemplate, ConflictPolicy = current.ConflictPolicy,
                    AfterSave = current.AfterSave, Notify = current.Notify,
                    RememberExtension = remember.IsChecked == true,
                    Quick = readQuick(), Automatic = readAutomatic(), AutoSaveTriggers = flags,
                };
            };
            return page;
        }

        private StackPanel SaveProfileEditor(ScreenshotSaveProfile value, string fallbackDirectory,
            out Func<ScreenshotSaveProfile> read, Func<AppSettings, ScreenshotSaveProfile> profileGetter)
        {
            var token = ContentCancellationToken;
            var page = Page();
            var directory = new TextBox { Text = value.Directory, PlaceholderText = fallbackDirectory };
            var format = new ComboBox
                { ItemsSource = new[] { "PNG", "JPEG", "BMP", "WebP", "AVIF" }, SelectedIndex = (int)value.Format };
            var quality = new CompactNumberInput { Minimum = 0, Maximum = 100, Value = value.Quality, Width = 100 };
            var name = new TextBox { Text = value.NameTemplate };
            var outputSize = OutputSizeEditor(value.OutputSize, out var readSize);
            var conflict = new ComboBox
                { ItemsSource = ScreenshotConflictLabels(), SelectedIndex = (int)value.ConflictPolicy };
            var after = new ComboBox { ItemsSource = SaveAfterLabels(), SelectedIndex = (int)value.AfterSave };
            var notify = new CheckBox
                { IsChecked = value.Notify, [!ContentProperty] = LiveText.Resource("ScreenshotSaveNotify") };
            var preview = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = AppTheme.Muted };
            var sample = new ScreenshotNameContext(new(2026, 1, 2, 12, 30, 45), "Window", "application", 1920, 1080)
                { Id = Guid.Empty };
            var browse = new Button { [!ContentProperty] = LiveText.Resource("Browse") };
            var open = new Button { [!ContentProperty] = LiveText.Resource("OpenFolder") };
            var directoryRow = new Grid { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = 6 };
            directoryRow.Children.Add(directory);
            Grid.SetColumn(browse, 1);
            directoryRow.Children.Add(browse);
            Grid.SetColumn(open, 2);
            directoryRow.Children.Add(open);
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
                    if (folders.Count > 0) directory.Text = folders[0].TryGetLocalPath();
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
            page.Children.Add(
                SettingRow("OutputDirectory", directoryRow, settings => profileGetter(settings).Directory));
            page.Children.Add(SettingRow("ScreenshotSaveFormat", format, settings => profileGetter(settings).Format));
            page.Children.Add(SettingRow("ScreenshotSaveQuality", quality,
                settings => profileGetter(settings).Quality));
            page.Children.Add(SettingRow("ScreenshotOutputSize", outputSize,
                settings => profileGetter(settings).OutputSize));
            page.Children.Add(SettingRow("ScreenshotNameTemplate", name,
                settings => profileGetter(settings).NameTemplate));
            page.Children.Add(new TextBlock
            {
                [!TextBlock.TextProperty] = LiveText.Resource("ScreenshotNameTemplateHint"),
                TextWrapping = TextWrapping.Wrap, Foreground = AppTheme.Muted,
            });
            page.Children.Add(Labeled("ScreenshotNamePreview", preview));
            page.Children.Add(SettingRow("ScreenshotConflictPolicy", conflict,
                settings => profileGetter(settings).ConflictPolicy));
            page.Children.Add(SettingRow("ScreenshotAfterSave", after, settings => profileGetter(settings).AfterSave));
            page.Children.Add(Setting(notify, settings => profileGetter(settings).Notify));

            directory.TextChanged += (_, _) => Preview();
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
                    preview.Text = Path.Combine(profile.ResolveDirectory(fallbackDirectory),
                        profile.CreateFileName(sample));
                }
                catch (Exception error) when (error is ArgumentException or NotSupportedException
                                                  or PathTooLongException)
                {
                    preview.Text = error.Message;
                }
            }

            ScreenshotSaveProfile Read()
            {
                return new()
                {
                    Directory = directory.Text ?? "", Format = (StaticImageFormat)Math.Max(0, format.SelectedIndex),
                    Quality = (int)ReadSettingsNumber(quality), NameTemplate = name.Text ?? "",
                    OutputSize = readSize(),
                    ConflictPolicy = (ScreenshotConflictPolicy)Math.Max(0, conflict.SelectedIndex),
                    AfterSave = (ScreenshotAfterSaveAction)Math.Max(0, after.SelectedIndex),
                    Notify = notify.IsChecked == true,
                };
            }

            void RefreshLanguage()
            {
                var selected = conflict.SelectedIndex;
                conflict.ItemsSource = ScreenshotConflictLabels();
                conflict.SelectedIndex = selected;
                selected = after.SelectedIndex;
                after.ItemsSource = SaveAfterLabels();
                after.SelectedIndex = selected;
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

        private static string[] SaveAfterLabels()
        {
            return
            [
                UiText.Get("ScreenshotAfterNone"), UiText.Get("ScreenshotAfterCopyImage"),
                UiText.Get("ScreenshotAfterCopyFile"), UiText.Get("ScreenshotAfterCopyPath"),
                UiText.Get("ScreenshotAfterOpenFolder"),
            ];
        }
    }
}

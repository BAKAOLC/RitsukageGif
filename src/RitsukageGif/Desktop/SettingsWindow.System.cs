using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SettingsWindow
    {
        private static readonly string[] SystemLanguageCodes = ["system", "zh-Hans", "en"];

        private static readonly string[] SystemLanguageLabels =
            ["SystemLanguage", "ChineseLanguage", "EnglishLanguage"];

        private static readonly string[] SystemOcrLayoutLabels =
            ["SystemOcrLayoutAuto", "SystemOcrLayoutSingle", "SystemOcrLayoutMulti"];

        private static readonly string[] SystemOcrPunctuationLabels =
            ["SystemOcrPunctuationNone", "SystemOcrPunctuationHalf", "SystemOcrPunctuationFull"];

        private static readonly string[] SystemOcrDialogLabels =
            ["SystemOcrDialogNever", "SystemOcrDialogRecognition", "SystemOcrDialogCopyAll", "SystemOcrDialogBoth"];

        private static readonly string[] SystemUpdateChannelLabels = ["SystemUpdateStable", "SystemUpdatePreview"];

        private StackPanel BuildSystemPage(AppSettings draft, StackPanel clipboard)
        {
            var contentToken = ContentCancellationToken;
            var page = Page();
            var general = Page();
            var languageCodes = SystemLanguageCodes;
            var language = new ComboBox
            {
                ItemsSource = SystemLanguageLabels.Select(UiText.Get)
                    .ToArray(),
                SelectedIndex = Math.Max(0, Array.IndexOf(languageCodes, draft.Language)),
            };
            var startup = SystemCheck("SystemStartWithWindows", draft.System.StartWithWindows,
                value => draft.System = draft.System with { StartWithWindows = value });
            var administrator = SystemCheck("SystemRunAsAdministrator", draft.System.RunAsAdministrator,
                value => draft.System = draft.System with { RunAsAdministrator = value });
            startup.IsEnabled = administrator.IsEnabled = OperatingSystem.IsWindows();
            if (!OperatingSystem.IsWindows())
            {
                startup.Bind(ToolTip.TipProperty, LiveText.Resource("SystemWindowsOnly"));
                administrator.Bind(ToolTip.TipProperty, LiveText.Resource("SystemWindowsOnly"));
            }
            else
            {
                administrator.Bind(ToolTip.TipProperty, LiveText.Resource("SystemAdministratorNextLaunch"));
            }

            language.SelectionChanged += (_, _) =>
            {
                if (language.SelectedIndex >= 0) draft.Language = languageCodes[language.SelectedIndex];
            };
            general.Children.Add(Labeled("Language", language));
            general.Children.Add(startup);
            general.Children.Add(administrator);
            general.Children.Add(SystemCheck("CloseToTray", draft.CloseToTray, value => draft.CloseToTray = value));
            general.Children.Add(SystemCheck("MinimizeToTray", draft.MinimizeToTray,
                value => draft.MinimizeToTray = value));
            page.Children.Add(SettingsSection("GeneralHeading", general));
            page.Children.Add(SettingsSection("ScreenshotClipboardHeading", clipboard));
            var tray = Page();
            var single = TrayActionBox(draft.System.TraySingleClick,
                value => draft.System = draft.System with { TraySingleClick = value });
            var doubleClick = TrayActionBox(draft.System.TrayDoubleClick,
                value => draft.System = draft.System with { TrayDoubleClick = value });
            var middle = TrayActionBox(draft.System.TrayMiddleClick,
                value => draft.System = draft.System with { TrayMiddleClick = value });
            single.IsEnabled = !OperatingSystem.IsMacOS();
            doubleClick.IsEnabled = middle.IsEnabled = OperatingSystem.IsWindows();
            if (!OperatingSystem.IsWindows())
            {
                doubleClick.Bind(ToolTip.TipProperty, LiveText.Resource("SystemWindowsOnly"));
                middle.Bind(ToolTip.TipProperty, LiveText.Resource("SystemWindowsOnly"));
            }

            tray.Children.Add(Labeled("SystemTraySingleClick", single));
            tray.Children.Add(Labeled("SystemTrayDoubleClick", doubleClick));
            tray.Children.Add(Labeled("SystemTrayMiddleClick", middle));
            var recognition = Page();
            var layout = new ComboBox
            {
                ItemsSource = SystemOcrLayoutLabels
                    .Select(UiText.Get).ToArray(),
                SelectedIndex = (int)draft.Recognition.Layout,
            };
            var punctuation = new ComboBox
            {
                ItemsSource = SystemOcrPunctuationLabels
                    .Select(UiText.Get).ToArray(),
                SelectedIndex = (int)draft.Recognition.Punctuation,
            };
            var popup = new ComboBox
            {
                ItemsSource = SystemOcrDialogLabels.Select(UiText.Get).ToArray(),
                SelectedIndex = (draft.Recognition.ShowDialogOnRecognition ? 1 : 0) |
                                (draft.Recognition.ShowDialogOnCopyAll ? 2 : 0),
            };
            layout.SelectionChanged += (_, _) =>
            {
                if (layout.SelectedIndex >= 0)
                    draft.Recognition = draft.Recognition with
                    {
                        Layout = (RecognitionTextLayout)layout.SelectedIndex,
                    };
            };
            punctuation.SelectionChanged += (_, _) =>
            {
                if (punctuation.SelectedIndex >= 0)
                    draft.Recognition = draft.Recognition with
                    {
                        Punctuation = (RecognitionPunctuation)punctuation.SelectedIndex,
                    };
            };
            popup.SelectionChanged += (_, _) =>
            {
                if (popup.SelectedIndex >= 0)
                    draft.Recognition = draft.Recognition with
                    {
                        ShowDialogOnRecognition = (popup.SelectedIndex & 1) != 0,
                        ShowDialogOnCopyAll = (popup.SelectedIndex & 2) != 0,
                    };
            };
            recognition.Children.Add(Labeled("SystemOcrLayout", layout));
            recognition.Children.Add(Labeled("SystemOcrDialogWhen", popup));
            recognition.Children.Add(Labeled("SystemOcrPunctuation", punctuation));
            var ocrLanguage = new ComboBox { IsEnabled = false };
            var languages = new List<OcrLanguage>();
            var loadingLanguages = false;
            var languagesLoaded = false;
            var refreshingLanguages = false;
            var additionalLanguage = "";
            ocrLanguage.SelectionChanged += (_, _) =>
            {
                // ReSharper disable once AccessToModifiedClosure
                if (!refreshingLanguages && ocrLanguage.SelectedIndex >= 0)
                    draft.Recognition = draft.Recognition with
                    {
                        LanguageTag = ocrLanguage.SelectedIndex == 0
                            ? ""
                            : ocrLanguage.SelectedIndex > languages.Count
                                // ReSharper disable once AccessToModifiedClosure
                                ? additionalLanguage
                                : languages[ocrLanguage.SelectedIndex - 1].Tag,
                    };
            };
            recognition.Children.Add(Labeled("RecognitionLanguage", ocrLanguage));
            recognition.AttachedToVisualTree += async (_, _) =>
            {
                if (loadingLanguages || languagesLoaded) return;
                loadingLanguages = true;
                try
                {
                    var capabilities = await new OcrRecognitionService().GetCapabilitiesAsync(contentToken);
                    if (contentToken.IsCancellationRequested || !IsVisible) return;
                    languages.AddRange(capabilities.Languages);
                    languagesLoaded = true;
                    ocrLanguage.IsEnabled = capabilities.Available;
                    RefreshLanguages();
                    if (!capabilities.Available) ToolTip.SetTip(ocrLanguage, UiText.Get("RecognitionUnavailable"));
                }
                catch (Exception error)
                {
                    if (!contentToken.IsCancellationRequested && IsVisible) ToolTip.SetTip(ocrLanguage, error.Message);
                }
                finally
                {
                    loadingLanguages = false;
                }
            };
            page.Children.Add(SettingsSection("RecognizeText", recognition));
            page.Children.Add(SettingsSection("SystemTrayHeading", tray));
            var updates = Page();
            updates.Children.Add(SystemCheck("AutomaticUpdates", draft.CheckForUpdates,
                value => draft.CheckForUpdates = value));
            var channel = new ComboBox
            {
                ItemsSource = SystemUpdateChannelLabels.Select(UiText.Get).ToArray(),
                SelectedIndex = (int)draft.System.UpdateChannel,
            };
            channel.SelectionChanged += (_, _) =>
            {
                if (channel.SelectedIndex >= 0)
                    draft.System = draft.System with
                    {
                        UpdateChannel = (UpdateChannel)channel.SelectedIndex,
                    };
            };
            updates.Children.Add(Labeled("SystemUpdateChannel", channel));
            AddSystemUpdateCheck(updates, draft);
            page.Children.Add(SettingsSection("SystemUpdatesHeading", updates));
            SubscribeContentLanguage(Refresh);
            Refresh();
            return page;

            void RefreshLanguages()
            {
                var selected = draft.Recognition.LanguageTag;
                List<string> labels =
                    [UiText.Get("SystemOcrFollowApplication"), .. languages.Select(value => value.DisplayName)];
                var index = languages.FindIndex(value =>
                    value.Tag.Equals(selected, StringComparison.OrdinalIgnoreCase));
                additionalLanguage = selected.Length > 0 && index < 0 ? selected : "";
                if (additionalLanguage.Length > 0) labels.Add(additionalLanguage);
                refreshingLanguages = true;
                try
                {
                    ocrLanguage.ItemsSource = labels;
                    ocrLanguage.SelectedIndex =
                        additionalLanguage.Length > 0 ? labels.Count - 1 : Math.Max(0, index + 1);
                }
                finally
                {
                    refreshingLanguages = false;
                }
            }

            void Refresh()
            {
                SetLabels(language, SystemLanguageLabels);
                SetLabels(layout, SystemOcrLayoutLabels);
                SetLabels(punctuation,
                    SystemOcrPunctuationLabels);
                SetLabels(popup,
                    SystemOcrDialogLabels);
                SetLabels(channel, SystemUpdateChannelLabels);
                RefreshLanguages();
            }
        }

        private static CheckBox SystemCheck(string key, bool selected, Action<bool> changed)
        {
            var box = new CheckBox { Name = key, [!ContentProperty] = LiveText.Resource(key), IsChecked = selected };
            box.IsCheckedChanged += (_, _) => changed(box.IsChecked == true);
            return box;
        }

        private ComboBox TrayActionBox(SystemTrayAction value, Action<SystemTrayAction> changed)
        {
            var values = Enum.GetValues<SystemTrayAction>();
            var box = new ComboBox
            {
                ItemsSource = values.Select(action => UiText.Get("SystemTrayAction" + action)).ToArray(),
                SelectedIndex = Array.IndexOf(values, value),
            };
            box.SelectionChanged += (_, _) =>
            {
                if (box.SelectedIndex >= 0) changed(values[box.SelectedIndex]);
            };

            SubscribeContentLanguage(Refresh);
            Refresh();
            return box;

            void Refresh()
            {
                SetLabels(box, [.. values.Select(action => "SystemTrayAction" + action)]);
            }
        }

        private static void SetLabels(ComboBox box, string[] keys)
        {
            var selected = box.SelectedIndex;
            box.ItemsSource = keys.Select(UiText.Get).ToArray();
            box.SelectedIndex = selected;
        }

        private void AddSystemUpdateCheck(StackPanel page, AppSettings draft)
        {
            var check = new Button { [!ContentProperty] = LiveText.Resource("CheckUpdate") };
            var download = new Button { [!ContentProperty] = LiveText.Resource("DownloadUpdate"), IsVisible = false };
            var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = AppTheme.Muted };
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8, Children = { check, download },
            };
            page.Children.Add(buttons);
            page.Children.Add(status);
            var cancellation = new CancellationTokenSource();
            RegisterContentCleanup(() =>
            {
                cancellation.Cancel();
                cancellation.Dispose();
            });
            UpdateRelease? release = null;
            download.Click += (_, _) =>
            {
                if (release is null) return;
                try
                {
                    UpdateChecker.OpenDownload(release.DownloadUrl);
                }
                catch (Exception error)
                {
                    status.Text = error.Message;
                }
            };
            check.Click += async (_, _) =>
            {
                check.IsEnabled = false;
                download.IsVisible = false;
                status.Text = UiText.Get("CheckingUpdate");
                try
                {
                    release = await UpdateChecker.CheckChannelAsync(draft.System.UpdateChannel, cancellation.Token);
                    if (cancellation.IsCancellationRequested) return;
                    status.Text = release is null ? UiText.Get("UpdateCurrent") : release.Version.ToString(4);
                    download.IsVisible = release is not null;
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                }
                catch (Exception)
                {
                    if (!cancellation.IsCancellationRequested) status.Text = UiText.Get("UpdateFailed");
                }
                finally
                {
                    if (!cancellation.IsCancellationRequested) check.IsEnabled = true;
                }
            };
        }
    }
}

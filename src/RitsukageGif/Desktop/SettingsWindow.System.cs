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

        private static readonly string[] SystemUpdateChannelLabels = ["SystemUpdateStable", "SystemUpdatePreview"];

        private static readonly string[] SystemExplorerModeLabels =
            ["SystemFeatureDisabled", "SystemExplorerImagesOnly", "SystemExplorerAllFiles"];

        private static readonly string[] SystemDesktopToolbarModeLabels =
            ["SystemDesktopFloatingBall", "SystemDesktopToolbar", "SystemFeatureDisabled"];

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
            var startup = Setting(SystemCheck("SystemStartWithWindows", draft.System.StartWithWindows,
                    value => draft.System = draft.System with { StartWithWindows = value }),
                static settings => settings.System.StartWithWindows);
            var startupHidden = Setting(SystemCheck("SystemStartupHidden", draft.System.StartupHidden,
                    value => draft.System = draft.System with { StartupHidden = value }),
                static settings => settings.System.StartupHidden);
            startupHidden.Margin = new(24, 0, 0, 0);
            startupHidden.IsEnabled = StartupRegistrationFactory.Supported && draft.System.StartWithWindows;
            startup.IsCheckedChanged += (_, _) =>
                startupHidden.IsEnabled = StartupRegistrationFactory.Supported && startup.IsChecked == true;
            var administrator = Setting(SystemCheck("SystemRunAsAdministrator", draft.System.RunAsAdministrator,
                    value => draft.System = draft.System with { RunAsAdministrator = value }),
                static settings => settings.System.RunAsAdministrator);
            startup.IsEnabled = StartupRegistrationFactory.Supported;
            administrator.IsEnabled = OperatingSystem.IsWindows();
            startup.Bind(ToolTip.TipProperty, LiveText.Resource("SystemStartupNextLogin"));
            administrator.Bind(ToolTip.TipProperty, LiveText.Resource(OperatingSystem.IsWindows()
                ? "SystemAdministratorNextLaunch"
                : "SystemWindowsOnly"));

            language.SelectionChanged += (_, _) =>
            {
                if (language.SelectedIndex >= 0) draft.Language = languageCodes[language.SelectedIndex];
            };
            general.Children.Add(SettingRow("Language", language, static settings => settings.Language));
            general.Children.Add(Setting(SystemCheck("SystemIgnoreCopyMacros", draft.System.IgnoreCopyMacros,
                    value => draft.System = draft.System with { IgnoreCopyMacros = value }),
                static settings => settings.System.IgnoreCopyMacros));
            general.Children.Add(startup);
            general.Children.Add(startupHidden);
            general.Children.Add(StartupStatusMessage(() => draft.System.StartWithWindows));
            general.Children.Add(administrator);
            var explorer = new ComboBox
            {
                ItemsSource = SystemExplorerModeLabels.Select(UiText.Get).ToArray(),
                SelectedIndex = (int)draft.System.ExplorerContextMenu,
                IsEnabled = OperatingSystem.IsWindows(),
            };
            explorer.SelectionChanged += (_, _) =>
            {
                if (explorer.SelectedIndex >= 0)
                    draft.System = draft.System with
                    {
                        ExplorerContextMenu = (ExplorerContextMenuMode)explorer.SelectedIndex,
                    };
            };
            general.Children.Add(SettingRow("SystemExplorerContextMode", explorer,
                static settings => settings.System.ExplorerContextMenu));
            var desktopToolbar = new ComboBox
            {
                ItemsSource = SystemDesktopToolbarModeLabels.Select(UiText.Get).ToArray(),
                SelectedIndex = (int)draft.System.DesktopToolbar.Mode,
            };
            desktopToolbar.SelectionChanged += (_, _) =>
            {
                if (desktopToolbar.SelectedIndex >= 0 &&
                    draft.System.DesktopToolbar.Mode != (DesktopToolbarMode)desktopToolbar.SelectedIndex)
                    draft.System = draft.System with
                    {
                        DesktopToolbar = draft.System.DesktopToolbar with
                        {
                            Mode = (DesktopToolbarMode)desktopToolbar.SelectedIndex, HiddenUntil = null,
                        },
                    };
            };
            general.Children.Add(SettingRow("SystemDesktopToolbarMode", desktopToolbar,
                static settings => settings.System.DesktopToolbar.Mode));
            general.Children.Add(Setting(
                SystemCheck("CloseToTray", draft.CloseToTray, value => draft.CloseToTray = value),
                static settings => settings.CloseToTray));
            general.Children.Add(Setting(SystemCheck("MinimizeToTray", draft.MinimizeToTray,
                value => draft.MinimizeToTray = value), static settings => settings.MinimizeToTray));
            page.Children.Add(SettingsSection("GeneralHeading", general));
            page.Children.Add(SettingsSection("ScreenshotClipboardHeading", clipboard));
            var tray = Page();
            var single = TrayActionBox(draft.System.TraySingleClick,
                value => draft.System = draft.System with { TraySingleClick = value });
            var doubleClick = TrayActionBox(draft.System.TrayDoubleClick,
                value => draft.System = draft.System with { TrayDoubleClick = value });
            var middle = TrayActionBox(draft.System.TrayMiddleClick,
                value => draft.System = draft.System with { TrayMiddleClick = value });
            RefreshTraySupport();
            PortableTrayState.Changed += RefreshTraySupport;
            RegisterContentCleanup(() => PortableTrayState.Changed -= RefreshTraySupport);

            tray.Children.Add(SettingRow("SystemTraySingleClick", single,
                static settings => settings.System.TraySingleClick));
            tray.Children.Add(SettingRow("SystemTrayDoubleClick", doubleClick,
                static settings => settings.System.TrayDoubleClick));
            tray.Children.Add(SettingRow("SystemTrayMiddleClick", middle,
                static settings => settings.System.TrayMiddleClick));
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
            var popupValues = new[]
            {
                draft.Recognition.ShowDialogOnRecognition, draft.Recognition.ShowDialogOnCopySelected,
                draft.Recognition.ShowDialogOnCopyAll,
            };
            var popup = ContentsSelector(popupValues,
                ["SystemOcrDialogRecognition", "SystemOcrDialogCopySelected", "SystemOcrDialogCopyAll"],
                () => draft.Recognition = draft.Recognition with
                {
                    ShowDialogOnRecognition = popupValues[0], ShowDialogOnCopySelected = popupValues[1],
                    ShowDialogOnCopyAll = popupValues[2],
                });
            popup.Name = "SystemOcrDialogWhen";
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
            recognition.Children.Add(SettingRow("SystemOcrLayout", layout,
                static settings => settings.Recognition.Layout));
            recognition.Children.Add(SettingRow("SystemOcrDialogWhen", popup,
                static settings => (settings.Recognition.ShowDialogOnRecognition,
                    settings.Recognition.ShowDialogOnCopySelected, settings.Recognition.ShowDialogOnCopyAll)));
            recognition.Children.Add(SettingRow("SystemOcrPunctuation", punctuation,
                static settings => settings.Recognition.Punctuation));
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
            recognition.Children.Add(SettingRow("RecognitionLanguage", ocrLanguage,
                static settings => settings.Recognition.LanguageTag));
            recognition.AttachedToVisualTree += async (_, _) =>
            {
                Activated += RefreshRecognitionLanguages;
                await LoadRecognitionLanguagesAsync();
            };
            recognition.DetachedFromVisualTree += (_, _) => Activated -= RefreshRecognitionLanguages;

            page.Children.Add(SettingsSection("RecognizeText", recognition));
            page.Children.Add(SettingsSection("TranslationTitle", BuildTranslationSettings(draft)));
            page.Children.Add(SettingsSection("SystemTrayHeading", tray));
            var updates = Page();
            updates.Children.Add(Setting(SystemCheck("AutomaticUpdates", draft.CheckForUpdates,
                value => draft.CheckForUpdates = value), static settings => settings.CheckForUpdates));
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
            updates.Children.Add(SettingRow("SystemUpdateChannel", channel,
                static settings => settings.System.UpdateChannel));
            AddSystemUpdateCheck(updates, draft);
            page.Children.Add(SettingsSection("SystemUpdatesHeading", updates));
            page.Children.Add(BuildNetworkSettings(draft));
            page.Children.Add(BuildDiagnosticsSettings());
            SubscribeContentLanguage(Refresh);
            Refresh();
            return page;

            void RefreshRecognitionLanguages(object? sender, EventArgs e)
            {
                languagesLoaded = false;
                _ = LoadRecognitionLanguagesAsync();
            }

            async Task LoadRecognitionLanguagesAsync()
            {
                if (loadingLanguages || languagesLoaded) return;
                loadingLanguages = true;
                try
                {
                    var capabilities = await new OcrRecognitionService().GetCapabilitiesAsync(contentToken);
                    if (contentToken.IsCancellationRequested || !IsVisible) return;
                    languages.Clear();
                    languages.AddRange(capabilities.Languages);
                    languagesLoaded = true;
                    ocrLanguage.IsEnabled = capabilities.Available;
                    RefreshLanguages();
                    ToolTip.SetTip(ocrLanguage, capabilities.Available ? null : UiText.Get("RecognitionUnavailable"));
                }
                catch (Exception error)
                {
                    if (!contentToken.IsCancellationRequested && IsVisible) ToolTip.SetTip(ocrLanguage, error.Message);
                }
                finally
                {
                    loadingLanguages = false;
                }
            }

            void RefreshTraySupport()
            {
                single.IsEnabled = PortableTrayState.Supports(TrayMouseGesture.SingleClick);
                doubleClick.IsEnabled = PortableTrayState.Supports(TrayMouseGesture.DoubleClick);
                middle.IsEnabled = PortableTrayState.Supports(TrayMouseGesture.MiddleClick);
                ToolTip.SetTip(single, single.IsEnabled ? null : UiText.Get("SystemTrayGestureUnavailable"));
                ToolTip.SetTip(middle, middle.IsEnabled ? null : UiText.Get("SystemTrayGestureUnavailable"));
                ToolTip.SetTip(doubleClick, doubleClick.IsEnabled ? null : UiText.Get("SystemTrayDoubleUnavailable"));
            }

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
                SetLabels(channel, SystemUpdateChannelLabels);
                SetLabels(explorer, SystemExplorerModeLabels);
                SetLabels(desktopToolbar, SystemDesktopToolbarModeLabels);
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
            SystemTrayAction[] values =
            [
                SystemTrayAction.None, SystemTrayAction.Screenshot, SystemTrayAction.ScreenshotAndPin,
                SystemTrayAction.OpenSettings, SystemTrayAction.ToggleGlobalShortcuts,
                SystemTrayAction.TogglePinnedImages,
            ];
            var box = new ComboBox
            {
                ItemsSource = values.Select(action => UiText.Get("SystemTrayNativeAction" + action)).ToArray(),
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
                SetLabels(box, [.. values.Select(action => "SystemTrayNativeAction" + action)]);
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
                    status.Text = release is null ? UiText.Get("UpdateCurrent") : release.DisplayVersion;
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

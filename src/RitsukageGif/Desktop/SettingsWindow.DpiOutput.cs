using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SettingsWindow
    {
        private StackPanel BuildDpiOutputSettings(AppSettings draft)
        {
            var page = Page();
            page.Children.Add(SettingRow("DpiOutputDefault",
                DpiOutputBox(draft.DpiOutput.LogicalPixels,
                    value => draft.DpiOutput = draft.DpiOutput with { LogicalPixels = value == true }, false),
                static settings => settings.DpiOutput.LogicalPixels));
            page.Children.Add(SettingRow("DpiOutputScreenshotSave",
                DpiOutputBox(draft.DpiOutput.ScreenshotSave,
                    value => draft.DpiOutput = draft.DpiOutput with { ScreenshotSave = value }),
                static settings => settings.DpiOutput.ScreenshotSave));
            page.Children.Add(SettingRow("DpiOutputScreenshotCopy",
                DpiOutputBox(draft.DpiOutput.ScreenshotCopy,
                    value => draft.DpiOutput = draft.DpiOutput with { ScreenshotCopy = value }, clipboard: true),
                static settings => settings.DpiOutput.ScreenshotCopy));
            page.Children.Add(SettingRow("DpiOutputLongScreenshotSave",
                DpiOutputBox(draft.DpiOutput.LongScreenshotSave,
                    value => draft.DpiOutput = draft.DpiOutput with { LongScreenshotSave = value }),
                static settings => settings.DpiOutput.LongScreenshotSave));
            page.Children.Add(SettingRow("DpiOutputLongScreenshotCopy",
                DpiOutputBox(draft.DpiOutput.LongScreenshotCopy,
                    value => draft.DpiOutput = draft.DpiOutput with { LongScreenshotCopy = value }, clipboard: true),
                static settings => settings.DpiOutput.LongScreenshotCopy));
            page.Children.Add(SettingRow("DpiOutputQuickRecording",
                DpiOutputBox(draft.DpiOutput.QuickRecording,
                    value => draft.DpiOutput = draft.DpiOutput with { QuickRecording = value }),
                static settings => settings.DpiOutput.QuickRecording));
            page.Children.Add(SettingRow("DpiOutputScreenshotRecording",
                DpiOutputBox(draft.DpiOutput.ScreenshotRecording,
                    value => draft.DpiOutput = draft.DpiOutput with { ScreenshotRecording = value }),
                static settings => settings.DpiOutput.ScreenshotRecording));
            page.Children.Add(SettingRow("DpiOutputVideoRecording",
                DpiOutputBox(draft.DpiOutput.VideoRecording,
                    value => draft.DpiOutput = draft.DpiOutput with { VideoRecording = value }),
                static settings => settings.DpiOutput.VideoRecording));
            return page;
        }

        private ComboBox DpiOutputBox(bool? value, Action<bool?> update, bool inherit = true, bool clipboard = false)
        {
            var keys = inherit
                ? new[] { "DpiOutputInherit", "DpiOutputPhysical", "DpiOutputLogical" }
                : ["DpiOutputPhysical", "DpiOutputLogical"];
            var start = inherit ? 1 : 0;
            var field = new ComboBox();
            field.Bind(ToolTip.TipProperty, LiveText.Resource(clipboard ? "DpiOutputClipboardHelp" : "DpiOutputHelp"));
            bool refreshing;
            var selected = value switch { true => start + 1, false => start, null => 0 };
            Refresh();
            SubscribeContentLanguage(Refresh);
            field.SelectionChanged += (_, _) =>
            {
                if (refreshing || field.SelectedIndex < 0) return;
                selected = field.SelectedIndex;
                update(selected < start ? null : selected > start);
                RequestLiveApply();
            };
            return field;

            void Refresh()
            {
                refreshing = true;
                field.ItemsSource = keys.Select(UiText.Get).ToArray();
                field.SelectedIndex = selected;
                refreshing = false;
            }
        }
    }
}

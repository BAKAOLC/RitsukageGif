using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SettingsWindow
    {
        private static StackPanel CaptureRecordingSettings(AppSettings draft, Func<int, string[], ComboBox> choice,
            out Action apply)
        {
            var preferences = draft.ScreenshotRecording.Normalize();
            var toolbar = choice((int)preferences.ToolbarCaptureMode,
                ["RecordingToolbarAsk", "RecordingToolbarShow", "RecordingToolbarHide"]);
            toolbar.Name = "RecordingToolbarCaptureMode";
            var exit = Toggle(preferences.AutoExitAfterEncoding);
            exit.Name = "RecordingAutoExitAfterEncoding";
            exit.Bind(ContentProperty, LiveText.Resource("RecordingReviewAutoExit"));
            var smooth = Toggle(preferences.InputEffects.SmoothTrack);
            smooth.Name = "RecordingSmoothMouseTrack";
            smooth.Bind(ContentProperty, LiveText.Resource("RecordingSmoothMouseTrack"));
            var simplify = Toggle(preferences.InputEffects.SimplifyMouseAnimation);
            simplify.Name = "RecordingSimplifyMouseAnimation";
            simplify.Bind(ContentProperty, LiveText.Resource("RecordingSimplifyMouseAnimation"));
            var history = new CompactNumberInput
            {
                Name = "RecordingKeyboardHistoryCount", Minimum = 1, Maximum = 10,
                Value = preferences.InputEffects.KeyboardMaxHistory, Width = 100,
            };
            var duration = new CompactNumberInput
            {
                Name = "RecordingKeyboardDisplaySeconds", Minimum = 1, Maximum = 10,
                Value = preferences.InputEffects.KeyboardKeepSeconds, Width = 100,
            };
            var characters = new CompactNumberInput
            {
                Name = "RecordingKeyboardMergeCharacters", Minimum = 1, Maximum = 50,
                Value = preferences.InputEffects.KeyboardMaxCharacters, Width = 100,
            };
            var fields = new StackPanel();
            fields.Children.Add(SettingRow("RecordingToolbarCaptureMode", toolbar,
                static settings => settings.ScreenshotRecording.ToolbarCaptureMode));
            fields.Children.Add(Setting(exit, static settings => settings.ScreenshotRecording.AutoExitAfterEncoding));
            fields.Children.Add(Setting(smooth,
                static settings => settings.ScreenshotRecording.InputEffects.SmoothTrack));
            fields.Children.Add(SettingRow("RecordingKeyboardHistoryCount", history,
                static settings => settings.ScreenshotRecording.InputEffects.KeyboardMaxHistory));
            fields.Children.Add(SettingRow("RecordingKeyboardDisplaySeconds", duration,
                static settings => settings.ScreenshotRecording.InputEffects.KeyboardKeepSeconds));
            fields.Children.Add(SettingRow("RecordingKeyboardMergeCharacters", characters,
                static settings => settings.ScreenshotRecording.InputEffects.KeyboardMaxCharacters));
            fields.Children.Add(Setting(simplify,
                static settings => settings.ScreenshotRecording.InputEffects.SimplifyMouseAnimation));
            apply = () => draft.ScreenshotRecording = draft.ScreenshotRecording with
            {
                AutoExitAfterEncoding = exit.IsChecked == true,
                ToolbarCaptureMode = (RecordingToolbarCaptureMode)Math.Max(0, toolbar.SelectedIndex),
                InputEffects = draft.ScreenshotRecording.InputEffects with
                {
                    SmoothTrack = smooth.IsChecked == true,
                    KeyboardMaxHistory = (int)ReadSettingsNumber(history),
                    KeyboardKeepSeconds = (int)ReadSettingsNumber(duration),
                    KeyboardMaxCharacters = (int)ReadSettingsNumber(characters),
                    SimplifyMouseAnimation = simplify.IsChecked == true,
                },
            };
            return SettingsSection("RecordingSettingsHeading", fields);
        }
    }
}

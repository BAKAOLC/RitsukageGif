using Avalonia.Controls;
using Avalonia.Layout;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SettingsWindow
    {
        private static readonly int[] ScreenshotHdpiTargets = [0, 100, 150, 200, 250, 300];

        private static readonly ScreenshotClipboardFileFormat[] ScreenshotClipboardFormats =
        [
            ScreenshotClipboardFileFormat.Disabled, ScreenshotClipboardFileFormat.Png,
            ScreenshotClipboardFileFormat.Jpeg, ScreenshotClipboardFileFormat.Webp,
            ScreenshotClipboardFileFormat.Bmp, ScreenshotClipboardFileFormat.Avif, ScreenshotClipboardFileFormat.Auto,
        ];

        private StackPanel ScreenshotClipboardPage(AppSettings draft, out Action apply, out StackPanel captureOptions)
        {
            var preferences = draft.ScreenshotClipboard.Normalize();
            captureOptions = new() { Spacing = 4 };
            var page = new StackPanel { Spacing = 8 };
            var format = new ComboBox
            {
                Name = "ScreenshotClipboardFileFormat", HorizontalAlignment = HorizontalAlignment.Stretch,
                ItemsSource = ClipboardFormatLabels(),
                SelectedIndex = Array.IndexOf(ScreenshotClipboardFormats, preferences.FileFormat),
            };
            var formatRow = SettingRow("ScreenshotClipboardFileFormat", format,
                static settings => settings.ScreenshotClipboard.FileFormat);
            page.Children.Add(formatRow);
            page.Children.Add(SettingRow("ScreenshotOutputSize",
                OutputSizeEditor(preferences.OutputSize, out var readSize),
                static settings => settings.ScreenshotClipboard.OutputSize));
            var hdpi = new ComboBox
            {
                Name = "ScreenshotHdpiReduction",
                ItemsSource = new[] { UiText.Get("ScreenshotHdpiNoReduction"), "100%", "150%", "200%", "250%", "300%" },
                SelectedIndex = Array.IndexOf(ScreenshotHdpiTargets, preferences.HdpiTargetPercent),
            };
            captureOptions.Children.Add(SettingRow("ScreenshotHdpiReduction", hdpi,
                static settings => settings.ScreenshotClipboard.HdpiTargetPercent));
            var paste = new CheckBox
            {
                Name = "ScreenshotPasteAfterCopy", IsChecked = preferences.PasteAfterCopy,
                IsEnabled = ScreenshotPasteGuard.IsSupported,
                [!ContentProperty] = LiveText.Resource("ScreenshotPasteAfterCopy"),
            };
            if (!ScreenshotPasteGuard.IsSupported)
                paste.Bind(ToolTip.TipProperty, LiveText.Resource("ScreenshotPastePlatformUnavailable"));
            else if (OperatingSystem.IsMacOS())
                paste.Bind(ToolTip.TipProperty, LiveText.Resource("ScreenshotPastePermissionRequired"));
            captureOptions.Children.Insert(0,
                Setting(paste, static settings => settings.ScreenshotClipboard.PasteAfterCopy));

            SubscribeContentLanguage(RefreshClipboardLanguage);
            apply = () => draft.ScreenshotClipboard = new()
            {
                FileFormat = ScreenshotClipboardFormats[Math.Max(0, format.SelectedIndex)],
                OutputSize = readSize(),
                HdpiTargetPercent = ScreenshotHdpiTargets[Math.Max(0, hdpi.SelectedIndex)],
                PasteAfterCopy = paste.IsChecked == true,
            };
            return page;

            void RefreshClipboardLanguage()
            {
                var selected = format.SelectedIndex;
                format.ItemsSource = ClipboardFormatLabels();
                format.SelectedIndex = selected;
                selected = hdpi.SelectedIndex;
                hdpi.ItemsSource = new[]
                    { UiText.Get("ScreenshotHdpiNoReduction"), "100%", "150%", "200%", "250%", "300%" };
                hdpi.SelectedIndex = selected;
            }
        }

        private static string[] ClipboardFormatLabels()
        {
            var template = UiText.Get("ScreenshotClipboardCopyFormat");
            return
            [
                UiText.Get("SystemFeatureDisabled"),
                string.Format(template, "PNG"), string.Format(template, "JPG"), string.Format(template, "WEBP"),
                string.Format(template, "BMP"), string.Format(template, "AVIF"), UiText.Get("ScreenshotClipboardAuto"),
            ];
        }
    }
}

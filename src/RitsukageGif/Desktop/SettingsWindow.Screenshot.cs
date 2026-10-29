using Avalonia.Controls;
using Avalonia.Layout;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SettingsWindow
    {
        private static readonly int[] ScreenshotHdpiTargets = [0, 100, 150, 200, 250, 300];

        private StackPanel ScreenshotClipboardPage(AppSettings draft, out Action apply, out StackPanel captureOptions)
        {
            var preferences = draft.ScreenshotClipboard.Normalize();
            captureOptions = new() { Spacing = 4 };
            var page = new StackPanel { Spacing = 8 };
            var mode = new ComboBox
            {
                Name = "ScreenshotClipboardMode", HorizontalAlignment = HorizontalAlignment.Stretch,
                ItemsSource = ClipboardModeLabels(),
                SelectedIndex = (int)preferences.Mode,
            };
            page.Children.Add(Labeled("ScreenshotClipboardCopyAs", mode));
            var format = new ComboBox
            {
                Name = "ScreenshotClipboardFileFormat", HorizontalAlignment = HorizontalAlignment.Stretch,
                ItemsSource = new[] { "PNG", "JPEG", "BMP", "WebP" },
                SelectedIndex = (int)preferences.FileFormat,
            };
            var formatRow = Labeled("ScreenshotClipboardFileFormat", format);
            page.Children.Add(formatRow);
            page.Children.Add(Labeled("ScreenshotOutputSize",
                OutputSizeEditor(preferences.OutputSize, out var readSize)));
            var hdpi = new ComboBox
            {
                Name = "ScreenshotHdpiReduction",
                ItemsSource = new[] { UiText.Get("ScreenshotHdpiNoReduction"), "100%", "150%", "200%", "250%", "300%" },
                SelectedIndex = Array.IndexOf(ScreenshotHdpiTargets, preferences.HdpiTargetPercent),
            };
            captureOptions.Children.Add(Labeled("ScreenshotHdpiReduction", hdpi));
            var paste = new CheckBox
            {
                Name = "ScreenshotPasteAfterCopy", IsChecked = preferences.PasteAfterCopy,
                IsEnabled = OperatingSystem.IsWindows(),
                [!ContentProperty] = LiveText.Resource("ScreenshotPasteAfterCopy"),
            };
            if (!OperatingSystem.IsWindows())
                paste.Bind(ToolTip.TipProperty, LiveText.Resource("ScreenshotPastePlatformUnavailable"));
            captureOptions.Children.Insert(0, paste);

            mode.SelectionChanged += (_, _) => UpdateVisibility();
            SubscribeContentLanguage(RefreshClipboardLanguage);
            UpdateVisibility();
            apply = () => draft.ScreenshotClipboard = new()
            {
                Mode = (ScreenshotClipboardMode)Math.Max(0, mode.SelectedIndex),
                FileFormat = (ScreenshotClipboardFileFormat)Math.Max(0, format.SelectedIndex),
                OutputSize = readSize(),
                HdpiTargetPercent = ScreenshotHdpiTargets[Math.Max(0, hdpi.SelectedIndex)],
                PasteAfterCopy = paste.IsChecked == true,
            };
            return page;

            void UpdateVisibility()
            {
                formatRow.IsVisible = mode.SelectedIndex == (int)ScreenshotClipboardMode.File;
            }

            void RefreshClipboardLanguage()
            {
                var selected = mode.SelectedIndex;
                mode.ItemsSource = ClipboardModeLabels();
                mode.SelectedIndex = selected;
                selected = hdpi.SelectedIndex;
                hdpi.ItemsSource = new[]
                    { UiText.Get("ScreenshotHdpiNoReduction"), "100%", "150%", "200%", "250%", "300%" };
                hdpi.SelectedIndex = selected;
            }
        }

        private static string[] ClipboardModeLabels()
        {
            return [UiText.Get("ScreenshotClipboardImage"), UiText.Get("ScreenshotClipboardFile")];
        }
    }
}

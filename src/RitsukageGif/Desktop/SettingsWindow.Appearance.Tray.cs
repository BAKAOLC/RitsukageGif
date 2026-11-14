using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SettingsWindow
    {
        private Grid TrayIconRow(AppSettings draft)
        {
            var picker = CreateAppearanceColorPicker("AppearanceTrayIconColor", draft.Appearance.TrayIconColor,
                false, () => draft.Appearance.ColorDialogStyle);
            picker.IsEnabled = draft.Appearance.TrayIcon == TrayIconAppearance.Monochrome;
            picker.Bind(AutomationProperties.NameProperty, LiveText.Resource("AppearanceTrayIconColor"));
            picker.Bind(ToolTip.TipProperty, LiveText.Resource("AppearanceTrayIconColor"));
            picker.ColorChanged += (_, _) =>
            {
                if (picker.IsEnabled)
                    draft.Appearance = draft.Appearance with { TrayIconColor = picker.Color.ToUInt32() };
            };

            using var stream = AssetLoader.Open(new("avares://RitsukageGif/!__AvaloniaDefaultWindowIcon"));
            var icon = new Bitmap(stream);
            var preview = new Image
                { Source = icon, Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center };
            RegisterContentCleanup(() =>
            {
                preview.Source = null;
                icon.Dispose();
            });

            var automatic = Option(TrayIconAppearance.Automatic, "AppearanceAutomatic", true);
            var original = Option(TrayIconAppearance.Original, "AppearanceOriginalIcon");
            original.Content = preview;
            var custom = Option(TrayIconAppearance.Monochrome, "AppearanceMonochromeIcon");
            var hidden = Option(TrayIconAppearance.Hidden, "AppearanceHidden", true);
            var monochrome = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 3, Children = { custom, picker },
            };
            var options = new WrapPanel
            {
                Orientation = Orientation.Horizontal, ItemSpacing = 7, LineSpacing = 2,
                Children = { automatic, original, monochrome, hidden },
            };
            return SettingRow("AppearanceTrayIcon", options,
                static settings => (settings.Appearance.TrayIcon, settings.Appearance.TrayIconColor));

            RadioButton Option(TrayIconAppearance mode, string key, bool showText = false)
            {
                var button = new RadioButton
                {
                    GroupName = "TrayIconAppearance", IsChecked = draft.Appearance.TrayIcon == mode,
                    VerticalAlignment = VerticalAlignment.Center, Padding = new(2, 0),
                    [!AutomationProperties.NameProperty] = LiveText.Resource(key),
                };
                if (showText) button.Bind(ContentProperty, LiveText.Resource(key));
                else button.Bind(ToolTip.TipProperty, LiveText.Resource(key));
                button.IsCheckedChanged += (_, _) =>
                {
                    if (button.IsChecked != true) return;
                    draft.Appearance = draft.Appearance with { TrayIcon = mode };
                    picker.IsEnabled = mode == TrayIconAppearance.Monochrome;
                };
                return button;
            }
        }
    }
}

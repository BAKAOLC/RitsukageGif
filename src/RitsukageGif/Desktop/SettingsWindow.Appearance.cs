using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SettingsWindow
    {
        private static StackPanel BuildAppearancePage(AppSettings draft)
        {
            var page = Page();
            var general = new StackPanel();
            page.Children.Add(MouseSettingsCard(general));

            Setting(Select(general, "AppearanceThemeMode", draft.Appearance.ThemeMode,
                    ["FollowSystem", "AppearanceLight", "AppearanceDark"],
                    value => draft.Appearance = draft.Appearance with { ThemeMode = value }),
                static settings => settings.Appearance.ThemeMode);
            var font = new AutoCompleteBox
            {
                Name = "AppearanceFont", Text = draft.Appearance.FontFamily,
                ItemsSource = FontManager.Current.SystemFonts.Select(item => item.Name)
                    .Order(StringComparer.CurrentCultureIgnoreCase).ToArray(),
                IsTextCompletionEnabled = true, PlaceholderText = UiText.Get("FollowSystem"),
            };
            font.TextChanged += (_, _) =>
            {
                draft.Appearance = draft.Appearance with { FontFamily = font.Text ?? "" };
                PreviewAppearance(draft.Appearance);
            };
            general.Children.Add(SettingRow("AppearanceFont", font, static settings => settings.Appearance.FontFamily));
            Setting(ColorRow(general, "AppearanceAccent", draft.Appearance.AccentColor,
                    value => draft.Appearance = draft.Appearance with { AccentColor = value }, false),
                static settings => settings.Appearance.AccentColor);
            Setting(ColorRow(general, "AppearanceLockedWindow", draft.Appearance.LockedWindowColor,
                    value => draft.Appearance = draft.Appearance with { LockedWindowColor = value }),
                static settings => settings.Appearance.LockedWindowColor);
            Setting(ColorRow(general, "AppearanceSelectionMask", draft.Appearance.SelectionMaskColor,
                    value => draft.Appearance = draft.Appearance with { SelectionMaskColor = value }),
                static settings => settings.Appearance.SelectionMaskColor);
            Setting(ColorRow(general, "AppearanceClickThroughShade", draft.Appearance.ClickThroughShadeColor,
                    value => draft.Appearance = draft.Appearance with { ClickThroughShadeColor = value }),
                static settings => settings.Appearance.ClickThroughShadeColor);
            var trayStyle = Setting(Select(general, "AppearanceTrayIcon", draft.Appearance.TrayIcon,
                    ["AppearanceAutomatic", "AppearanceOriginalIcon", "AppearanceMonochromeIcon", "AppearanceHidden"],
                    value => draft.Appearance = draft.Appearance with { TrayIcon = value }),
                static settings => settings.Appearance.TrayIcon);
            var trayColor = Setting(ColorRow(general, "AppearanceTrayIconColor", draft.Appearance.TrayIconColor,
                    value => draft.Appearance = draft.Appearance with { TrayIconColor = value }, false),
                static settings => settings.Appearance.TrayIconColor);
            trayColor.IsEnabled = draft.Appearance.TrayIcon == TrayIconAppearance.Monochrome;
            trayStyle.SelectionChanged += (_, _) =>
                trayColor.IsEnabled = draft.Appearance.TrayIcon == TrayIconAppearance.Monochrome;
            Setting(Select(general, "AppearanceToolbarIconSize", draft.Appearance.ToolbarIconSize,
                    ["AppearanceSmall", "AppearanceMedium", "AppearanceLarge"],
                    value => draft.Appearance = draft.Appearance with { ToolbarIconSize = value }),
                static settings => settings.Appearance.ToolbarIconSize);
            var colorStyle = Setting(Select(general, "AppearanceColorDialog", draft.Appearance.ColorDialogStyle,
                    ["AppearanceModern", "AppearanceSystem"],
                    value => draft.Appearance = draft.Appearance with { ColorDialogStyle = value }),
                static settings => settings.Appearance.ColorDialogStyle);
            if (!OperatingSystem.IsWindows())
            {
                colorStyle.SelectedIndex = 0;
                colorStyle.IsEnabled = false;
                general.Children.Add(new TextBlock
                {
                    [!TextBlock.TextProperty] = LiveText.Resource("AppearanceSystemColorWindowsOnly"),
                    TextWrapping = TextWrapping.Wrap, Foreground = AppTheme.Muted,
                });
            }

            Setting(Select(general, "AppearanceSaveDialog", draft.Appearance.SaveDialogStyle,
                    ["AppearanceModern", "AppearanceSystem"],
                    value => draft.Appearance = draft.Appearance with { SaveDialogStyle = value }),
                static settings => settings.Appearance.SaveDialogStyle);
            var selection = new StackPanel();
            page.Children.Add(new TextBlock
            {
                [!TextBlock.TextProperty] = LiveText.Resource("ScreenshotSettings"), Margin = new(0, 12, 0, 0),
                FontWeight = FontWeight.SemiBold,
            });
            page.Children.Add(MouseSettingsCard(selection));
            var width = new NumericUpDown
            {
                Name = "AppearanceSelectionBorderWidth", Minimum = 1, Maximum = 8, Increment = .5m,
                Value = (decimal)draft.Appearance.SelectionBorderWidth, Width = 100,
            };
            width.ValueChanged += (_, _) =>
            {
                if (width.Value is { } current)
                    draft.Appearance = draft.Appearance with { SelectionBorderWidth = (double)current };
            };
            selection.Children.Add(SettingRow("AppearanceSelectionBorderWidth", width,
                static settings => settings.Appearance.SelectionBorderWidth));
            var border = CreateAppearanceColorPicker("AppearanceSelectionBorderColor",
                draft.Appearance.SelectionBorderColor ?? draft.Appearance.AccentColor, true,
                () => draft.Appearance.ColorDialogStyle);
            var follow = new CheckBox
            {
                [!ContentProperty] = LiveText.Resource("AppearanceFollowTheme"),
                IsChecked = draft.Appearance.SelectionBorderColor is null,
            };
            border.IsEnabled = follow.IsChecked != true;
            follow.IsCheckedChanged += (_, _) =>
            {
                border.IsEnabled = follow.IsChecked != true;
                draft.Appearance = draft.Appearance with
                {
                    SelectionBorderColor = follow.IsChecked == true ? null : border.Color.ToUInt32(),
                };
            };
            border.ColorChanged += (_, _) =>
            {
                if (follow.IsChecked != true)
                    draft.Appearance = draft.Appearance with { SelectionBorderColor = border.Color.ToUInt32() };
            };
            selection.Children.Add(SettingRow("AppearanceSelectionBorderColor",
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { follow, border } },
                static settings => settings.Appearance.SelectionBorderColor));
            Setting(Select(selection, "AppearanceAnchors", draft.Appearance.SelectionAnchors,
                    ["AppearanceHidden", "AppearanceFourAnchors", "AppearanceEightAnchors"],
                    value => draft.Appearance = draft.Appearance with { SelectionAnchors = value }),
                static settings => settings.Appearance.SelectionAnchors);
            var animation = new CheckBox
            {
                [!ContentProperty] = LiveText.Resource("AppearanceSelectionAnimation"),
                IsChecked = draft.Appearance.AnimateSelection,
            };
            animation.IsCheckedChanged += (_, _) =>
                draft.Appearance = draft.Appearance with { AnimateSelection = animation.IsChecked == true };
            selection.Children.Add(Setting(animation, static settings => settings.Appearance.AnimateSelection));
            return page;

            ColorPicker ColorRow(StackPanel target, string key, uint value, Action<uint> change, bool alpha = true)
            {
                var color = CreateAppearanceColorPicker(key, value, alpha, () => draft.Appearance.ColorDialogStyle);
                color.ColorChanged += (_, _) =>
                {
                    change(color.Color.ToUInt32());
                    if (key == "AppearanceAccent") PreviewAppearance(draft.Appearance);
                };
                target.Children.Add(Labeled(key, color));
                return color;
            }

            ComboBox Select<T>(StackPanel target, string key, T value, string[] labels, Action<T> change)
                where T : struct, Enum
            {
                var options = Enum.GetValues<T>();
                var box = new ComboBox
                {
                    Name = key, ItemsSource = labels.Select(UiText.Get).ToArray(),
                    SelectedIndex = Array.IndexOf(options, value),
                };
                box.SelectionChanged += (_, _) =>
                {
                    if (box.SelectedIndex < 0) return;
                    change(options[box.SelectedIndex]);
                    if (key == "AppearanceThemeMode") PreviewAppearance(draft.Appearance);
                };
                target.Children.Add(Labeled(key, box));
                return box;
            }
        }

        private static AppearanceColorPicker CreateAppearanceColorPicker(string name, uint color, bool alpha,
            Func<AppearanceDialogStyle> style)
        {
            var picker = new AppearanceColorPicker
            {
                DialogStyleProvider = style, Name = name, Color = Color.FromUInt32(color), Width = 100, Height = 28,
                MinHeight = 0,
                IsAlphaEnabled = alpha, IsAlphaVisible = alpha, IsColorPaletteVisible = false,
                IsComponentTextInputVisible = true, IsHexInputVisible = true,
                HexInputAlphaPosition = AlphaComponentPosition.Leading,
            };
            picker.Styles.Add(new AnnotationColorPickerStyles());
            var swatch = new Border { Background = new SolidColorBrush(picker.Color), Margin = new(1) };
            picker.Content = swatch;
            picker.ColorChanged += (_, _) => swatch.Background = new SolidColorBrush(picker.Color);
            return picker;
        }
    }
}

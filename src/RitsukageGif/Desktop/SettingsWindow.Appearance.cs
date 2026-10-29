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

            Select(general, "AppearanceThemeMode", draft.Appearance.ThemeMode,
                ["FollowSystem", "AppearanceLight", "AppearanceDark"],
                value => draft.Appearance = draft.Appearance with { ThemeMode = value });
            var font = new AutoCompleteBox
            {
                Name = "AppearanceFont", Text = draft.Appearance.FontFamily,
                ItemsSource = FontManager.Current.SystemFonts.Select(item => item.Name)
                    .Order(StringComparer.CurrentCultureIgnoreCase).ToArray(),
                IsTextCompletionEnabled = true, Watermark = UiText.Get("FollowSystem"),
            };
            font.TextChanged += (_, _) =>
            {
                draft.Appearance = draft.Appearance with { FontFamily = font.Text ?? "" };
                PreviewAppearance(draft.Appearance);
            };
            general.Children.Add(Labeled("AppearanceFont", font));
            ColorRow(general, "AppearanceAccent", draft.Appearance.AccentColor,
                value => draft.Appearance = draft.Appearance with { AccentColor = value }, false);
            ColorRow(general, "AppearanceLockedWindow", draft.Appearance.LockedWindowColor,
                value => draft.Appearance = draft.Appearance with { LockedWindowColor = value });
            ColorRow(general, "AppearanceSelectionMask", draft.Appearance.SelectionMaskColor,
                value => draft.Appearance = draft.Appearance with { SelectionMaskColor = value });
            ColorRow(general, "AppearanceClickThroughShade", draft.Appearance.ClickThroughShadeColor,
                value => draft.Appearance = draft.Appearance with { ClickThroughShadeColor = value });
            var trayStyle = Select(general, "AppearanceTrayIcon", draft.Appearance.TrayIcon,
                ["AppearanceAutomatic", "AppearanceOriginalIcon", "AppearanceMonochromeIcon", "AppearanceHidden"],
                value => draft.Appearance = draft.Appearance with { TrayIcon = value });
            var trayColor = ColorRow(general, "AppearanceTrayIconColor", draft.Appearance.TrayIconColor,
                value => draft.Appearance = draft.Appearance with { TrayIconColor = value }, false);
            trayColor.IsEnabled = draft.Appearance.TrayIcon == TrayIconAppearance.Monochrome;
            trayStyle.SelectionChanged += (_, _) =>
                trayColor.IsEnabled = draft.Appearance.TrayIcon == TrayIconAppearance.Monochrome;
            Select(general, "AppearanceToolbarIconSize", draft.Appearance.ToolbarIconSize,
                ["AppearanceSmall", "AppearanceMedium", "AppearanceLarge"],
                value => draft.Appearance = draft.Appearance with { ToolbarIconSize = value });
            var colorStyle = Select(general, "AppearanceColorDialog", draft.Appearance.ColorDialogStyle,
                ["AppearanceModern", "AppearanceSystem"],
                value => draft.Appearance = draft.Appearance with { ColorDialogStyle = value });
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

            Select(general, "AppearanceSaveDialog", draft.Appearance.SaveDialogStyle,
                ["AppearanceModern", "AppearanceSystem"],
                value => draft.Appearance = draft.Appearance with { SaveDialogStyle = value });
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
            selection.Children.Add(Labeled("AppearanceSelectionBorderWidth", width));
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
            selection.Children.Add(Labeled("AppearanceSelectionBorderColor",
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { follow, border } }));
            Select(selection, "AppearanceAnchors", draft.Appearance.SelectionAnchors,
                ["AppearanceHidden", "AppearanceFourAnchors", "AppearanceEightAnchors"],
                value => draft.Appearance = draft.Appearance with { SelectionAnchors = value });
            var animation = new CheckBox
            {
                [!ContentProperty] = LiveText.Resource("AppearanceSelectionAnimation"),
                IsChecked = draft.Appearance.AnimateSelection,
            };
            animation.IsCheckedChanged += (_, _) =>
                draft.Appearance = draft.Appearance with { AnimateSelection = animation.IsChecked == true };
            selection.Children.Add(animation);
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

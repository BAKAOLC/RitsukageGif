namespace RitsukageGif.Desktop
{
    public sealed record AppearancePreferences
    {
        public AppearanceThemeMode ThemeMode { get; set; } = AppearanceThemeMode.Light;
        public string FontFamily { get; set; } = "";
        public uint AccentColor { get; set; } = 0xFF0064FA;
        public uint LockedWindowColor { get; set; } = 0xFFFFA000;
        public uint SelectionMaskColor { get; set; } = 0x66000000;
        public uint ClickThroughShadeColor { get; set; } = 0x663AA167;
        public TrayIconAppearance TrayIcon { get; set; } = TrayIconAppearance.Original;
        public uint TrayIconColor { get; set; } = 0xFFFFFFFF;
        public ToolbarIconSize ToolbarIconSize { get; set; } = ToolbarIconSize.Medium;
        public AppearanceDialogStyle ColorDialogStyle { get; set; }
        public AppearanceDialogStyle SaveDialogStyle { get; set; } = AppearanceDialogStyle.System;
        public double SelectionBorderWidth { get; set; } = 1.5;
        public uint? SelectionBorderColor { get; set; }
        public SelectionAnchorMode SelectionAnchors { get; set; } = SelectionAnchorMode.Eight;
        public bool AnimateSelection { get; set; } = true;

        public AppearancePreferences Normalize()
        {
            return this with
            {
                ThemeMode = Enum.IsDefined(ThemeMode) ? ThemeMode : AppearanceThemeMode.Light,
                // ReSharper disable once ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
                FontFamily = FontFamily?.Trim() is { Length: <= 256 } font ? font : "",
                AccentColor = AccentColor | 0xFF000000,
                TrayIcon = Enum.IsDefined(TrayIcon) ? TrayIcon : TrayIconAppearance.Original,
                ToolbarIconSize = Enum.IsDefined(ToolbarIconSize) ? ToolbarIconSize : ToolbarIconSize.Medium,
                ColorDialogStyle = Enum.IsDefined(ColorDialogStyle) ? ColorDialogStyle : AppearanceDialogStyle.Modern,
                SaveDialogStyle = Enum.IsDefined(SaveDialogStyle) ? SaveDialogStyle : AppearanceDialogStyle.System,
                SelectionBorderWidth =
                double.IsFinite(SelectionBorderWidth) ? Math.Clamp(SelectionBorderWidth, 1, 8) : 1.5,
                SelectionAnchors = Enum.IsDefined(SelectionAnchors) ? SelectionAnchors : SelectionAnchorMode.Eight,
            };
        }
    }
}

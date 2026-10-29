using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace RitsukageGif.Desktop
{
    internal sealed partial class AppTheme
    {
        private const string DefaultFontFamily = "Segoe UI, Microsoft YaHei UI, Noto Sans CJK SC, DejaVu Sans";
        private static Application? _appearanceApplication;
        internal static AppearancePreferences CurrentAppearance { get; private set; } = new();

        internal static void Apply(AppearancePreferences preferences)
        {
            CurrentAppearance = preferences.Normalize();
            if (Application.Current is not { } app) return;
            if (!ReferenceEquals(_appearanceApplication, app))
            {
                if (_appearanceApplication is not null)
                    _appearanceApplication.ActualThemeVariantChanged -= AppearanceThemeChanged;
                _appearanceApplication = app;
                app.ActualThemeVariantChanged += AppearanceThemeChanged;
            }

            var theme = CurrentAppearance.ThemeMode switch
            {
                AppearanceThemeMode.Light => ThemeVariant.Light,
                AppearanceThemeMode.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };
            app.RequestedThemeVariant = theme;
            app.Resources["ApplicationFont"] = new FontFamily(string.IsNullOrEmpty(CurrentAppearance.FontFamily)
                ? DefaultFontFamily
                : CurrentAppearance.FontFamily);
            ApplyPalette(app);
        }

        private static void AppearanceThemeChanged(object? sender, EventArgs args)
        {
            if (sender is Application app) ApplyPalette(app);
        }

        private static void ApplyPalette(Application app)
        {
            app.Resources["ApplicationThemeVariant"] = app.ActualThemeVariant;
            var dark = app.ActualThemeVariant == ThemeVariant.Dark;
            Surface.Color = Color.Parse(dark ? "#202124" : "#FFFFFF");
            Workspace.Color = Color.Parse(dark ? "#17181A" : "#F5F5F5");
            SurfaceTint.Color = Color.Parse(dark ? "#2B2D31" : "#F8F9FA");
            Outline.Color = Color.Parse(dark ? "#35FFFFFF" : "#141C1F23");
            Ink.Color = Color.Parse(dark ? "#F0F1F3" : "#1C1F23");
            Muted.Color = Color.Parse(dark ? "#ACAFB7" : "#9E1C1F23");
            Accent.Color = Color.FromUInt32(CurrentAppearance.AccentColor);
            AccentSoft.Color = Color.FromArgb(38, Accent.Color.R, Accent.Color.G, Accent.Color.B);
            foreach (var key in new[] { "SemiColorPrimary", "SemiColorPrimaryPointerover", "SemiColorPrimaryActive" })
            {
                if (!app.TryGetResource(key, app.ActualThemeVariant, out var resource) ||
                    resource is not SolidColorBrush brush)
                    continue;
                var factor = key == "SemiColorPrimary" ? 1 :
                    key.EndsWith("Active", StringComparison.Ordinal) ? .75 : .88;
                brush.Color = Color.FromRgb((byte)(Accent.Color.R * factor), (byte)(Accent.Color.G * factor),
                    (byte)(Accent.Color.B * factor));
            }
        }
    }
}

namespace RitsukageGif.Desktop
{
    internal sealed partial class SettingsWindow
    {
        private static void PreviewAppearance(AppearancePreferences preferences)
        {
            AppTheme.Apply(preferences);
        }
    }
}

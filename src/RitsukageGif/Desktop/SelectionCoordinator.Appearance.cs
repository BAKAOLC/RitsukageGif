namespace RitsukageGif.Desktop
{
    internal sealed partial class SelectionCoordinator
    {
        internal AppearancePreferences AppearancePreferences { get; private set; } = new();

        internal void InitializeAppearancePreferences(AppearancePreferences preferences)
        {
            if (_started) throw new InvalidOperationException();
            AppearancePreferences = preferences.Normalize();
        }
    }
}

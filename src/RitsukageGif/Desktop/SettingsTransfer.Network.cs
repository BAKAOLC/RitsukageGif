namespace RitsukageGif.Desktop
{
    internal static partial class SettingsTransfer
    {
        private static void ValidateNetworkPreferences(NetworkPreferences? preferences)
        {
            if (preferences is null) throw Invalid("Network");
            try
            {
                preferences.Validate();
            }
            catch (ArgumentException)
            {
                throw Invalid("Network");
            }
        }
    }
}

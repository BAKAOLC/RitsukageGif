using System.Text.Json;

namespace RitsukageGif.Desktop
{
    public sealed partial record AppSettings
    {
        public NetworkPreferences Network { get; set; } = new();

        internal static NetworkPreferences ReadPersistedNetwork()
        {
            string json;
            try
            {
                json = File.ReadAllText(FilePath);
            }
            catch (FileNotFoundException)
            {
                return new();
            }
            catch (DirectoryNotFoundException)
            {
                return new();
            }

            var persisted = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings)
                            ?? throw new InvalidDataException("The persisted settings document is empty.");
            persisted.Network.Validate();
            return persisted.Network.Normalize();
        }
    }
}

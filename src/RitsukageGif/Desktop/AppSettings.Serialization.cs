using System.Text.Json;

namespace RitsukageGif.Desktop
{
    public sealed partial record AppSettings
    {
        internal static AppSettings Deserialize(string json)
        {
            return JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings) ?? new();
        }
    }
}

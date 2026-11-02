using System.Text.Json;

namespace RitsukageGif.Desktop
{
    internal static partial class SettingsTransfer
    {
        static partial void ValidateCodeDetectionProperty(string parent, JsonProperty property, string path)
        {
            if (parent == "CapturePreferences" && property.Name == nameof(CapturePreferences.CodeDetection))
                ValidateEnum<CaptureCodeDetectionMode>(property.Value, path);
        }
    }
}

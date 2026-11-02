using System.Text.Json;

namespace RitsukageGif.Desktop
{
    internal static partial class SettingsTransfer
    {
        static partial void ValidateCaptureDoubleClickProperty(string parent, JsonProperty property, string path)
        {
            if (parent == "CapturePreferences" && property.Name == nameof(CapturePreferences.DoubleClickAction))
                ValidateEnum<CaptureDoubleClickAction>(property.Value, path);
        }
    }
}

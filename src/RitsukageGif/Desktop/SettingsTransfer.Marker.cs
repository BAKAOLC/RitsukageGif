using System.Text.Json;

namespace RitsukageGif.Desktop
{
    internal static partial class SettingsTransfer
    {
        static partial void ValidateMarkerProperties(JsonElement properties, string path)
        {
            if (properties.TryGetProperty(nameof(ScreenshotAnnotationProperties.MarkerPath), out var mode))
                ValidateEnum<AnnotationMarkerPath>(mode, path + ".MarkerPath");
            if (properties.TryGetProperty(nameof(ScreenshotAnnotationProperties.MarkerBlend), out var blend))
                ValidateEnum<AnnotationMarkerBlend>(blend, path + ".MarkerBlend");
        }
    }
}

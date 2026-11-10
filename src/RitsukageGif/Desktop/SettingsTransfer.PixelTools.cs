using System.Text.Json;

namespace RitsukageGif.Desktop
{
    internal static partial class SettingsTransfer
    {
        static partial void ValidatePixelToolProperties(JsonElement properties, string path)
        {
            if (properties.TryGetProperty(nameof(ScreenshotAnnotationProperties.PixelPath), out var value))
                ValidateEnum<AnnotationPixelPath>(value, path + ".PixelPath");
            if (properties.TryGetProperty(nameof(ScreenshotAnnotationProperties.MosaicMode), out var mode))
                ValidateEnum<AnnotationMosaicMode>(mode, path + ".MosaicMode");
            if (properties.TryGetProperty(nameof(ScreenshotAnnotationProperties.InpaintingRadius), out var radius))
                ValidateNumber(radius, path + ".InpaintingRadius", 1, 32);
        }
    }
}

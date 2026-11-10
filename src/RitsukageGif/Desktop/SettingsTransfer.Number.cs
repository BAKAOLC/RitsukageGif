using System.Text.Json;

namespace RitsukageGif.Desktop
{
    internal static partial class SettingsTransfer
    {
        static partial void ValidateNumberProperties(JsonElement properties, string path)
        {
            if (properties.TryGetProperty("NumberStyle", out var style))
                ValidateEnum<AnnotationNumberStyle>(style, path + ".NumberStyle");
            if (properties.TryGetProperty("NumberFormat", out var format))
                ValidateEnum<AnnotationNumberFormat>(format, path + ".NumberFormat");
            if (properties.TryGetProperty("NumberCaptionScale", out var scale))
                ValidateNumber(scale, path + ".NumberCaptionScale", .2, 1);
        }
    }
}

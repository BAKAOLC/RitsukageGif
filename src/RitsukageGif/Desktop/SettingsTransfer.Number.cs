using System.Text.Json;
using Avalonia.Media;

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
                ValidateNumber(scale, path + ".NumberCaptionScale", .25, 4);
            if (properties.TryGetProperty("NumberOutlineColor", out var color) &&
                (color.ValueKind != JsonValueKind.String || !Color.TryParse(color.GetString(), out _)))
                throw Invalid(path + ".NumberOutlineColor");
        }
    }
}

using System.Text.Json;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal static partial class SettingsTransfer
    {
        private static void ValidateAnnotationPreferences(JsonElement root)
        {
            if (!root.TryGetProperty("Annotation", out var annotation)) return;
            ValidateStyle(annotation, "Annotation");
            foreach (var property in annotation.EnumerateObject())
            {
                var path = "Annotation." + property.Name;
                switch (property.Name)
                {
                    case "MaximumBrushThickness": ValidateNumber(property.Value, path, 1, 1024); break;
                    case "QuickSettingsPadding": ValidateNumber(property.Value, path, 0, 64); break;
                    case "PencilCursor": ValidateEnum<AnnotationPencilCursor>(property.Value, path); break;
                    case "ControlPointVisibility":
                        ValidateEnum<AnnotationControlPointVisibility>(property.Value, path); break;
                    case "PencilShiftMode": ValidateEnum<AnnotationPencilShiftMode>(property.Value, path); break;
                    case "PolylineJoin": ValidateEnum<PenLineJoin>(property.Value, path); break;
                    case "PolylineCap": ValidateEnum<PenLineCap>(property.Value, path); break;
                    case "TextWrapMode": ValidateEnum<AnnotationTextWrapMode>(property.Value, path); break;
                    case "Toolbar": ValidateToolbarPreferences(property.Value, path); break;
                    case "PinnedLastTool":
                        if (property.Value.ValueKind != JsonValueKind.String ||
                            !Enum.TryParse<ScreenshotTool>(property.Value.GetString(), out var tool) ||
                            !Enum.IsDefined(tool)) throw Invalid(path);
                        break;
                    case "ColorPalette":
                        if (property.Value.ValueKind != JsonValueKind.Array ||
                            property.Value.GetArrayLength() is < 1 or > 32)
                            throw Invalid(path);
                        // ReSharper disable once ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator
                        foreach (var color in property.Value.EnumerateArray())
                            if (color.ValueKind != JsonValueKind.String || !Color.TryParse(color.GetString(), out _))
                                throw Invalid(path);
                        break;
                }
            }

            if (!annotation.TryGetProperty("ToolStyles", out var styles)) return;
            if (styles.ValueKind != JsonValueKind.Object) throw Invalid("Annotation.ToolStyles");
            foreach (var style in styles.EnumerateObject())
            {
                if (!Enum.TryParse<ScreenshotTool>(style.Name, out var tool) || !Enum.IsDefined(tool) ||
                    style.Name != tool.ToString()) throw Invalid("Annotation.ToolStyles." + style.Name);
                ValidateStyle(style.Value, "Annotation.ToolStyles." + style.Name);
            }
        }

        private static void ValidateEnum<T>(JsonElement value, string path) where T : struct, Enum
        {
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number) ||
                !Enum.IsDefined(typeof(T), number)) throw Invalid(path);
        }

        private static void ValidateStyle(JsonElement style, string path)
        {
            if (style.ValueKind != JsonValueKind.Object) throw Invalid(path);
            foreach (var property in style.EnumerateObject())
                switch (property.Name)
                {
                    case "Properties":
                        ValidateProperties(property.Value, path + ".Properties");
                        break;
                    case "Color":
                        if (property.Value.ValueKind != JsonValueKind.String ||
                            !Color.TryParse(property.Value.GetString(), out _))
                            throw Invalid(path + ".Color");
                        break;
                    case "Thickness":
                    case "FontSize":
                    case "MosaicSize":
                        ValidateNumber(property.Value, path + "." + property.Name,
                            property.Name == "MosaicSize" ? 2 : 1, property.Name == "MosaicSize" ? 256 : 1024,
                            property.Name == "MosaicSize");
                        break;
                }
        }

        private static void ValidateProperties(JsonElement properties, string path)
        {
            if (properties.ValueKind != JsonValueKind.Object) throw Invalid(path);
            foreach (var property in properties.EnumerateObject())
            {
                var name = path + "." + property.Name;
                switch (property.Name)
                {
                    case "TextPadding": ValidateNumber(property.Value, name, 0, 128); break;
                    case "TextOutlineWidth": ValidateNumber(property.Value, name, 0, 16); break;
                    case "WrapWidth": ValidateNumber(property.Value, name, 0, 8192); break;
                    case "Opacity": ValidateNumber(property.Value, name, .01, 1); break;
                    case "Magnification": ValidateNumber(property.Value, name, 1.1, 16); break;
                    case "Number": ValidateNumber(property.Value, name, 0, 999999, true); break;
                    case "BlurRadius": ValidateNumber(property.Value, name, 1, 256, true); break;
                    case "Anchor": ValidateNumber(property.Value, name, 0, 8, true); break;
                    case "ImageWidth": ValidateNumber(property.Value, name, 1, 8192); break;
                    case "TextBackground":
                    case "TextOutline":
                        if (property.Value.ValueKind != JsonValueKind.String ||
                            !Color.TryParse(property.Value.GetString(), out _)) throw Invalid(name);
                        break;
                }
            }
        }

        private static void ValidateNumber(JsonElement value, string path, double minimum, double maximum,
            bool integer = false)
        {
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number) ||
                !double.IsFinite(number) || number < minimum || number > maximum ||
                (integer && !value.TryGetInt32(out _))) throw Invalid(path);
        }
    }
}

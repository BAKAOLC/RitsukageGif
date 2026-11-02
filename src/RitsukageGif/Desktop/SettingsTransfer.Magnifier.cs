using System.Text.Json;

namespace RitsukageGif.Desktop
{
    internal static partial class SettingsTransfer
    {
        static partial void ValidateMagnifierProperties(JsonElement properties, string path)
        {
            foreach (var property in properties.EnumerateObject())
            {
                var name = path + "." + property.Name;
                switch (property.Name)
                {
                    case "Magnification": ValidateNumber(property.Value, name, 1, 8); break;
                    case "MagnifierLink": ValidateEnum<AnnotationMagnifierLink>(property.Value, name); break;
                }
            }
        }
    }
}

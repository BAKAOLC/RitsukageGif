using System.Text.Json;

namespace RitsukageGif.Desktop
{
    internal static partial class SettingsTransfer
    {
        private static void ValidateOutputProperty(string parent, JsonProperty property, string path)
        {
            if (property.Name == "OutputSize" &&
                (parent == "ScreenshotClipboard" || parent == "ScreenshotSave" ||
                 parent.StartsWith("ScreenshotSave.", StringComparison.Ordinal)))
            {
                if (property.Value.ValueKind != JsonValueKind.Object) throw Invalid(path);
                var size = new ScreenshotOutputSize();
                foreach (var field in property.Value.EnumerateObject())
                {
                    var name = path + "." + field.Name;
                    switch (field.Name)
                    {
                        case "Mode":
                            if (field.Value.ValueKind != JsonValueKind.Number ||
                                !field.Value.TryGetInt32(out var mode) ||
                                !Enum.IsDefined((ScreenshotOutputSizeMode)mode)) throw Invalid(name);
                            size.Mode = (ScreenshotOutputSizeMode)mode;
                            break;
                        case "Percent":
                            if (field.Value.ValueKind != JsonValueKind.Number ||
                                !field.Value.TryGetDouble(out var percent) || !double.IsFinite(percent) ||
                                percent is <= 0 or > 10000) throw Invalid(name);
                            size.Percent = percent;
                            break;
                        case "Width":
                        case "Height":
                            if (field.Value.ValueKind != JsonValueKind.Number ||
                                !field.Value.TryGetInt32(out var length) ||
                                length is <= 0 or > 128 * 1024 * 1024) throw Invalid(name);
                            if (field.Name == "Width") size.Width = length;
                            else size.Height = length;
                            break;
                    }
                }

                if (size.Mode is ScreenshotOutputSizeMode.Exact or ScreenshotOutputSizeMode.Fit &&
                    (long)size.Width * size.Height > 128L * 1024 * 1024) throw Invalid(path);
            }

            if (parent != "ScreenshotClipboard") return;
            if ((property.Name == "HdpiTargetPercent" &&
                 (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetInt32(out var target) ||
                  target is not (0 or 100 or 150 or 200 or 250 or 300))) ||
                property is
                {
                    Name: "PasteAfterCopy", Value.ValueKind: not (JsonValueKind.True or JsonValueKind.False),
                })
                throw Invalid(path);
        }
    }
}

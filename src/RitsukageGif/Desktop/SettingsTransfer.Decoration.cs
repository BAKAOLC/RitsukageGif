using System.Text.Json;

namespace RitsukageGif.Desktop
{
    internal static partial class SettingsTransfer
    {
        private static void ValidateDecorationProperty(string parent, JsonProperty property, string path)
        {
            if (parent != "CapturePreferences") return;
            switch (property.Name)
            {
                case "FrameRoundEnabled" or "KeepDecoration":
                    if (property.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                        throw Invalid(path);
                    return;
                case "FrameRoundRadius":
                    if (property.Value.ValueKind != JsonValueKind.Number ||
                        !property.Value.TryGetDouble(out var radius) ||
                        !double.IsFinite(radius) || radius is < 0 or > 60) throw Invalid(path);
                    return;
                case "FrameDecoration":
                    if (property.Value.ValueKind != JsonValueKind.Object) throw Invalid(path);
                    foreach (var field in property.Value.EnumerateObject())
                    {
                        var name = path + "." + field.Name;
                        switch (field.Name)
                        {
                            case "Kind":
                                if (field.Value.ValueKind != JsonValueKind.Number ||
                                    !field.Value.TryGetInt32(out var value) ||
                                    (CaptureDecorationKind)value is not
                                    (CaptureDecorationKind.Shadow or CaptureDecorationKind.Border))
                                    throw Invalid(name);
                                break;
                            case "ShadowStrength" or "BorderStrength":
                                if (field.Value.ValueKind != JsonValueKind.Number ||
                                    !field.Value.TryGetInt32(out var strength) ||
                                    strength is < 1 or > 50) throw Invalid(name);
                                break;
                            case "ShadowColor" or "BorderColor":
                                if (field.Value.ValueKind != JsonValueKind.Number || !field.Value.TryGetUInt32(out _))
                                    throw Invalid(name);
                                break;
                        }
                    }

                    break;
            }
        }
    }
}

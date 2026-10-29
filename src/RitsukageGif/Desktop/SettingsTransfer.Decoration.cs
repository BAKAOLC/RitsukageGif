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
                case "RememberedDecoration":
                    if (property.Value.ValueKind != JsonValueKind.Object) throw Invalid(path);
                    var kind = CaptureDecorationKind.None;
                    var strength = 0;
                    foreach (var field in property.Value.EnumerateObject())
                    {
                        var name = path + "." + field.Name;
                        switch (field.Name)
                        {
                            case "Kind":
                                if (field.Value.ValueKind != JsonValueKind.Number ||
                                    !field.Value.TryGetInt32(out var value) ||
                                    !Enum.IsDefined((CaptureDecorationKind)value)) throw Invalid(name);
                                kind = (CaptureDecorationKind)value;
                                break;
                            case "Strength":
                                if (field.Value.ValueKind != JsonValueKind.Number ||
                                    !field.Value.TryGetInt32(out strength) ||
                                    strength is < 0 or > 50) throw Invalid(name);
                                break;
                            case "Color":
                                if (field.Value.ValueKind != JsonValueKind.Number || !field.Value.TryGetUInt32(out _))
                                    throw Invalid(name);
                                break;
                            case "CornerRadius":
                                if (field.Value.ValueKind != JsonValueKind.Number ||
                                    !field.Value.TryGetDouble(out var corner) || corner != 0)
                                    throw Invalid(name);
                                break;
                        }
                    }

                    if (kind != CaptureDecorationKind.None && strength == 0) throw Invalid(path + ".Strength");
                    break;
            }
        }
    }
}

using System.Text.Json;

namespace RitsukageGif.Desktop
{
    internal static partial class SettingsTransfer
    {
        static partial void ValidateCaptureDoubleClickProperty(string parent, JsonProperty property, string path)
        {
            if (parent != "CapturePreferences") return;
            switch (property.Name)
            {
                case nameof(CapturePreferences.DoubleClickAction):
                case nameof(CapturePreferences.MiddleClickAction):
                    ValidateEnum<CaptureClickAction>(property.Value, path);
                    break;
                case nameof(CapturePreferences.WheelFunctions):
                    ValidateEnum<CaptureWheelFunctions>(property.Value, path);
                    break;
                case nameof(CapturePreferences.InformationUnit):
                    ValidateEnum<SelectionSizeUnit>(property.Value, path);
                    break;
                case nameof(CapturePreferences.InformationItems):
                    if (property.Value.ValueKind != JsonValueKind.Number ||
                        !property.Value.TryGetInt32(out var items) ||
                        (items & ~(int)SelectionInfoItems.All) != 0) throw Invalid(path);
                    break;
                case nameof(CapturePreferences.SoundTiming):
                    if (property.Value.ValueKind != JsonValueKind.Number ||
                        !property.Value.TryGetInt32(out var timing) ||
                        (timing & ~(int)ScreenshotSoundTiming.All) != 0) throw Invalid(path);
                    break;
            }
        }
    }
}

using System.Text.Json;

namespace RitsukageGif.Desktop
{
    internal static partial class SettingsTransfer
    {
        static partial void ValidateSystemFeaturesProperty(string parent, JsonProperty property, string path)
        {
            if (parent == "System" && property is
                    { Name: nameof(SystemPreferences.DesktopToolbar), Value.ValueKind: JsonValueKind.Object } &&
                property.Value.TryGetProperty(nameof(DesktopToolbarPreferences.Primary), out var primary) &&
                property.Value.TryGetProperty(nameof(DesktopToolbarPreferences.More), out var more) &&
                primary.ValueKind == JsonValueKind.Array && more.ValueKind == JsonValueKind.Array)
            {
                var ids = primary.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String)
                    .Select(value => value.GetString()).ToHashSet(StringComparer.Ordinal);
                if (more.EnumerateArray().Any(value =>
                        value.ValueKind == JsonValueKind.String && ids.Contains(value.GetString())))
                    throw Invalid(path);
            }

            if (parent == "System" && property.Name == nameof(SystemPreferences.ExplorerContextMenu))
                ValidateEnum<ExplorerContextMenuMode>(property.Value, path);
            if (parent != "System." + nameof(SystemPreferences.DesktopToolbar)) return;
            switch (property.Name)
            {
                case nameof(DesktopToolbarPreferences.Mode):
                    ValidateEnum<DesktopToolbarMode>(property.Value, path);
                    break;
                case nameof(DesktopToolbarPreferences.Primary):
                case nameof(DesktopToolbarPreferences.More):
                    if (property.Value.ValueKind != JsonValueKind.Array) throw Invalid(path);
                    var ids = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var value in property.Value.EnumerateArray())
                        if (value.ValueKind != JsonValueKind.String || value.GetString() is not { } id ||
                            !DesktopToolbarPreferences.IsSupported(id) || !ids.Add(id))
                            throw Invalid(path);
                    break;
            }
        }
    }
}

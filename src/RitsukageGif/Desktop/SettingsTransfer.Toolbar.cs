using System.Text.Json;

namespace RitsukageGif.Desktop
{
    internal static partial class SettingsTransfer
    {
        private static void ValidateToolbarPreferences(JsonElement toolbar, string path)
        {
            if (toolbar.ValueKind != JsonValueKind.Object) throw Invalid(path);
            var properties = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in toolbar.EnumerateObject())
            {
                var name = path + "." + property.Name;
                if (!properties.Add(property.Name)) throw Invalid(name);
                switch (property.Name)
                {
                    case "Columns": ValidateToolbarColumns(property.Value, name); break;
                    case "Actions": ValidateToolbarActions(property.Value, name); break;
                    default: throw Invalid(name);
                }
            }
        }

        private static void ValidateToolbarColumns(JsonElement columns, string path)
        {
            var toolCount = Enum.GetValues<ScreenshotTool>().Length;
            if (columns.ValueKind != JsonValueKind.Array || columns.GetArrayLength() > toolCount) throw Invalid(path);
            var tools = new HashSet<ScreenshotTool>();
            foreach (var column in columns.EnumerateArray())
            {
                if (column.ValueKind != JsonValueKind.Array || column.GetArrayLength() > toolCount) throw Invalid(path);
                foreach (var tool in column.EnumerateArray())
                {
                    ValidateEnum<ScreenshotTool>(tool, path);
                    if (!tools.Add((ScreenshotTool)tool.GetInt32()) || tools.Count > toolCount) throw Invalid(path);
                }
            }
        }

        private static void ValidateToolbarActions(JsonElement actions, string path)
        {
            if (actions.ValueKind != JsonValueKind.Array ||
                actions.GetArrayLength() > Enum.GetValues<ToolbarActionId>().Length) throw Invalid(path);
            var identifiers = new HashSet<ToolbarActionId>();
            foreach (var action in actions.EnumerateArray())
            {
                if (action.ValueKind != JsonValueKind.Object || !action.TryGetProperty("Id", out var id) ||
                    !action.TryGetProperty("Visibility", out var visibility)) throw Invalid(path);
                ValidateEnum<ToolbarActionId>(id, path + ".Id");
                ValidateEnum<ToolbarButtonVisibility>(visibility, path + ".Visibility");
                if (!identifiers.Add((ToolbarActionId)id.GetInt32())) throw Invalid(path + ".Id");
                var properties = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in action.EnumerateObject())
                    if (!properties.Add(property.Name) || property.Name is not ("Id" or "Visibility"))
                        throw Invalid(path + "." + property.Name);
            }
        }
    }
}

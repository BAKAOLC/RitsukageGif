using System.Text.Json;

namespace RitsukageGif.Desktop
{
    internal static partial class SettingsTransfer
    {
        private static void ValidateToolbarPreferences(JsonElement toolbar, string path)
        {
            if (toolbar.ValueKind != JsonValueKind.Object) throw Invalid(path);
            if (!toolbar.TryGetProperty(nameof(ToolbarPreferences.SchemaRevision), out var revision) ||
                revision.ValueKind != JsonValueKind.Number ||
                !revision.TryGetInt32(out var schemaRevision) ||
                schemaRevision != ToolbarPreferences.CurrentSchemaRevision)
                throw Invalid(path + "." + nameof(ToolbarPreferences.SchemaRevision));
            var properties = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in toolbar.EnumerateObject())
            {
                var name = path + "." + property.Name;
                if (!properties.Add(property.Name)) throw Invalid(name);
                switch (property.Name)
                {
                    case nameof(ToolbarPreferences.SchemaRevision): break;
                    case "Columns": ValidateToolbarColumns(property.Value, name); break;
                    case "Actions": ValidateToolbarActions(property.Value, name); break;
                    case nameof(ToolbarPreferences.ColumnValues):
                        ValidateToolbarColumnValues(toolbar, property.Value, name);
                        break;
                    default: throw Invalid(name);
                }
            }
        }

        private static void ValidateToolbarColumns(JsonElement columns, string path)
        {
            var toolCount = ScreenshotToolCatalog.Registered.Count;
            if (columns.ValueKind != JsonValueKind.Array || columns.GetArrayLength() > toolCount) throw Invalid(path);
            var tools = new HashSet<ScreenshotTool>();
            foreach (var column in columns.EnumerateArray())
            {
                if (column.ValueKind != JsonValueKind.Array || column.GetArrayLength() > toolCount) throw Invalid(path);
                foreach (var tool in column.EnumerateArray())
                {
                    ValidateEnum<ScreenshotTool>(tool, path);
                    if (ScreenshotToolCatalog.Registered.All(definition =>
                            definition.Tool != (ScreenshotTool)tool.GetInt32()) ||
                        !tools.Add((ScreenshotTool)tool.GetInt32()) || tools.Count > toolCount) throw Invalid(path);
                }
            }
        }

        private static void ValidateToolbarColumnValues(JsonElement toolbar, JsonElement values, string path)
        {
            if (values.ValueKind != JsonValueKind.Object) throw Invalid(path);
            var entries = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in values.EnumerateObject())
            {
                var name = path + "." + entry.Name;
                if (!entries.Add(entry.Name) || !Enum.TryParse<ScreenshotTool>(entry.Name, out var first) ||
                    first.ToString() != entry.Name || !ScreenshotToolCatalog.IsRegistered(first))
                    throw Invalid(name);
                ValidateEnum<ScreenshotTool>(entry.Value, name);
                var selected = (ScreenshotTool)entry.Value.GetInt32();
                if (first == selected || !ScreenshotToolCatalog.IsRegistered(selected) ||
                    !toolbar.TryGetProperty(nameof(ToolbarPreferences.Columns), out var columns) ||
                    columns.ValueKind != JsonValueKind.Array ||
                    !columns.EnumerateArray().Any(column => column.ValueKind == JsonValueKind.Array &&
                                                            column.GetArrayLength() > 1 &&
                                                            column[0].ValueKind == JsonValueKind.Number &&
                                                            column[0].TryGetInt32(out var anchor) &&
                                                            anchor == (int)first &&
                                                            column.EnumerateArray().Any(tool =>
                                                                tool.ValueKind == JsonValueKind.Number &&
                                                                tool.TryGetInt32(out var candidate) &&
                                                                candidate == (int)selected)))
                    throw Invalid(name);
            }
        }

        private static void ValidateToolbarActions(JsonElement actions, string path)
        {
            if (actions.ValueKind != JsonValueKind.Array ||
                actions.GetArrayLength() > ToolbarActionCatalog.Configurable.Count) throw Invalid(path);
            var identifiers = new HashSet<ToolbarActionId>();
            foreach (var action in actions.EnumerateArray())
            {
                if (action.ValueKind != JsonValueKind.Object || !action.TryGetProperty("Id", out var id) ||
                    !action.TryGetProperty("Visibility", out var visibility)) throw Invalid(path);
                ValidateEnum<ToolbarActionId>(id, path + ".Id");
                if (!ToolbarActionCatalog.Get((ToolbarActionId)id.GetInt32()).Configurable)
                    throw Invalid(path + ".Id");
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

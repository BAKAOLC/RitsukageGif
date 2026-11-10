using System.Text.Json.Serialization;

namespace RitsukageGif.Desktop
{
    public sealed record ToolbarPreferences
    {
        public const int CurrentSchemaRevision = 1;

        public ToolbarPreferences() : this(CurrentSchemaRevision)
        {
        }

        [JsonConstructor]
        // ReSharper disable once ConvertToPrimaryConstructor
        public ToolbarPreferences(int schemaRevision = 0)
        {
            SchemaRevision = schemaRevision;
        }

        // ReSharper disable once TypeWithSuspiciousEqualityIsUsedInRecord.Global
        public List<List<ScreenshotTool>> Columns { get; set; } = CreateDefaultColumns();

        // ReSharper disable once TypeWithSuspiciousEqualityIsUsedInRecord.Global
        public List<ToolbarActionPreference> Actions { get; set; } = CreateDefaultActions();

        public int SchemaRevision { get; set; }

        public ToolbarPreferences Merge(ToolbarPreferences changes, bool? tools)
        {
            return (tools switch
            {
                true => this with { Columns = changes.Columns },
                false => this with { Actions = changes.Actions },
                null => changes,
            }).Normalize();
        }

        public ToolbarPreferences Normalize()
        {
            if (SchemaRevision != CurrentSchemaRevision) return new();
            var toolCount = ScreenshotToolCatalog.Registered.Count;
            var tools = new HashSet<ScreenshotTool>();
            var columns = new List<List<ScreenshotTool>>();
            // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
            foreach (var column in (Columns ?? CreateDefaultColumns()).Take(toolCount))
            {
                // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                if (column is null) continue;
                columns.Add([
                    .. column.Where(tool => ScreenshotToolCatalog.IsRegistered(tool) && tools.Add(tool))
                        .Take(toolCount),
                ]);
            }

            var identifiers = new HashSet<ToolbarActionId>();
            var actions = new List<ToolbarActionPreference>();
            // ReSharper disable once ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator, NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
            foreach (var action in Actions ?? [])
                // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                if (action is not null &&
                    ToolbarActionCatalog.Configurable.Any(definition => definition.Id == action.Id) &&
                    Enum.IsDefined(action.Visibility) &&
                    identifiers.Add(action.Id))
                    actions.Add(action with { });
            // ReSharper disable once ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator
            foreach (var action in CreateDefaultActions())
                if (identifiers.Add(action.Id))
                    actions.Add(action);
            return this with { Columns = columns, Actions = actions };
        }

        private static List<List<ScreenshotTool>> CreateDefaultColumns()
        {
            return [.. ScreenshotToolCatalog.DefaultGroups.Select(group => group.ToList())];
        }

        private static List<ToolbarActionPreference> CreateDefaultActions()
        {
            return
            [
                .. ToolbarActionCatalog.Configurable.Select(definition => new ToolbarActionPreference
                {
                    Id = definition.Id, Visibility = definition.DefaultVisibility,
                }),
            ];
        }
    }
}

namespace RitsukageGif.Desktop
{
    public sealed record ToolbarPreferences
    {
        // ReSharper disable once TypeWithSuspiciousEqualityIsUsedInRecord.Global
        public List<List<ScreenshotTool>> Columns { get; set; } = CreateDefaultColumns();

        // ReSharper disable once TypeWithSuspiciousEqualityIsUsedInRecord.Global
        public List<ToolbarActionPreference> Actions { get; set; } = CreateDefaultActions();

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
            var toolCount = Enum.GetValues<ScreenshotTool>().Length;
            var tools = new HashSet<ScreenshotTool>();
            var columns = new List<List<ScreenshotTool>>();
            // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
            foreach (var column in (Columns ?? CreateDefaultColumns()).Take(toolCount))
            {
                // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                if (column is null) continue;
                columns.Add([.. column.Where(tool => Enum.IsDefined(tool) && tools.Add(tool)).Take(toolCount)]);
            }

            var identifiers = new HashSet<ToolbarActionId>();
            var actions = new List<ToolbarActionPreference>();
            // ReSharper disable once ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator, NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
            foreach (var action in Actions ?? [])
                // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                if (action is not null && Enum.IsDefined(action.Id) && Enum.IsDefined(action.Visibility) &&
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
            return
            [
                [ScreenshotTool.Rectangle, ScreenshotTool.Ellipse],
                [ScreenshotTool.Brush, ScreenshotTool.Highlight],
                [ScreenshotTool.Arrow, ScreenshotTool.Line, ScreenshotTool.Polyline],
                [ScreenshotTool.Text],
                [ScreenshotTool.Number],
                [ScreenshotTool.Mosaic, ScreenshotTool.Blur],
                [ScreenshotTool.Eraser, ScreenshotTool.StrokeEraser],
            ];
        }

        private static List<ToolbarActionPreference> CreateDefaultActions()
        {
            return
            [
                .. Enum.GetValues<ToolbarActionId>().Select(id => new ToolbarActionPreference
                {
                    Id = id,
                    Visibility = id <= ToolbarActionId.Cancel
                        ? ToolbarButtonVisibility.Toolbar
                        : ToolbarButtonVisibility.MoreMenu,
                }),
            ];
        }
    }
}

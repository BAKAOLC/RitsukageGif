namespace RitsukageGif.Desktop
{
    internal static partial class BuiltinShortcuts
    {
        private static IEnumerable<BuiltinShortcutDefinition> PinLayoutDefinitions()
        {
            foreach (var command in PinnedImageArrangementCommands.Catalog)
                yield return new("builtin.pinned." + command.Action, "BuiltinShortcutPinnedContext", command.TextKey,
                    "", command.Action);
            yield return new("builtin.pinned.selection-save", "BuiltinShortcutPinnedContext", "PinnedSelectionSave",
                "", "selection-save");
        }
    }
}

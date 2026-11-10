namespace RitsukageGif.Desktop
{
    internal sealed record ToolbarActionDefinition(
        ToolbarActionId Id,
        string NameKey,
        string IconKey,
        ToolbarButtonVisibility DefaultVisibility,
        SelectionCommand? Command = null,
        bool Configurable = true);
}

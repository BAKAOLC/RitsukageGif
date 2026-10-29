namespace RitsukageGif.Desktop
{
    public sealed record ToolbarActionPreference
    {
        public ToolbarActionId Id { get; set; }
        public ToolbarButtonVisibility Visibility { get; set; }
    }
}

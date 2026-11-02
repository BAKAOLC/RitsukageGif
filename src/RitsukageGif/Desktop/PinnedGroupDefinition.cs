namespace RitsukageGif.Desktop
{
    public sealed record PinnedGroupDefinition
    {
        public string Id { get; init; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "";
        public string? Color { get; set; }

        internal bool IsValid => (Id is { Length: 0 } || Guid.TryParseExact(Id, "N", out _)) &&
                                 Name is { Length: <= 32767 } && (Id.Length == 0 || Name.Length != 0) &&
                                 (Color is null || Avalonia.Media.Color.TryParse(Color, out _));

        internal PinnedGroupDefinition Normalize()
        {
            var normalized = Avalonia.Media.Color.TryParse(Color, out var color) ? color.ToString() : null;
            return Color == normalized ? this : this with { Color = normalized };
        }
    }
}

namespace RitsukageGif.Desktop
{
    public sealed record GlobalActionBinding
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "";
        public GlobalActionKind Kind { get; set; } = GlobalActionKind.Screenshot;
        public string Shortcut { get; set; } = "";
        public bool ShowInTray { get; set; } = true;
        public bool Enabled { get; set; } = true;

        // ReSharper disable once TypeWithSuspiciousEqualityIsUsedInRecord.Global
        public List<string> IgnoredPrograms { get; set; } = [];
        public string Parameter { get; set; } = "";
        public string PresetName { get; set; } = "";
        public Dictionary<string, string> Arguments { get; set; } = new(StringComparer.Ordinal);
        public string Script { get; set; } = "";
    }
}

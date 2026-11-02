namespace RitsukageGif.Contracts
{
    public sealed record ColorFormatDefinition
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string Template { get; init; } = "";
        public bool Enabled { get; init; } = true;
    }
}

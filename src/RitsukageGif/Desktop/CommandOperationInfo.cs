namespace RitsukageGif.Desktop
{
    public sealed record CommandOperationInfo(
        string Id,
        string Kind,
        string State,
        string? Source,
        DateTimeOffset Started);
}

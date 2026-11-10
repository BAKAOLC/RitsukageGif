namespace RitsukageGif.Desktop
{
    public sealed record CommandActionInfo(
        string? Id,
        string Kind,
        string Name,
        bool Enabled,
        bool Available,
        bool FileParameter,
        string? Parameter = null,
        string? PresetName = null,
        IReadOnlyDictionary<string, string>? Arguments = null,
        string? UnavailableReason = null,
        string? Script = null);
}

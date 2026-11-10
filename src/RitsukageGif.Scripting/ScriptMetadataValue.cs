namespace RitsukageGif.Scripting
{
    public sealed record ScriptMetadataValue(
        string Kind,
        string? Text = null,
        double? Number = null,
        bool? Boolean = null);
}

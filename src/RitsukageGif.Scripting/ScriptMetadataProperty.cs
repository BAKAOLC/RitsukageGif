namespace RitsukageGif.Scripting
{
    public sealed record ScriptMetadataProperty(
        string Name,
        string TypeName,
        bool Optional,
        ScriptMetadataValue? DefaultValue);
}

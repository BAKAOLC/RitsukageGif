namespace RitsukageGif.Scripting
{
    public sealed record ScriptBindingMetadata(
        int Version,
        ScriptMetadataGlobal[] Globals,
        ScriptMetadataTable[] Tables,
        ScriptMetadataHost[] Hosts,
        ScriptMetadataSchema[] Schemas,
        ScriptMetadataEnum[] Enums,
        Dictionary<string, string> Declarations);
}

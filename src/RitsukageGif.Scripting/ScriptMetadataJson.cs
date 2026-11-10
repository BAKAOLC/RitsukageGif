using System.Text.Json.Serialization;

namespace RitsukageGif.Scripting
{
    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(ScriptBindingMetadata))]
    internal partial class ScriptMetadataJson : JsonSerializerContext;
}

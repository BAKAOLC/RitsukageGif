using System.Text.Json.Serialization;

namespace RitsukageGif.Desktop
{
    [JsonSerializable(typeof(bool))]
    [JsonSerializable(typeof(PinnedImageState[]))]
    [JsonSerializable(typeof(PinnedDocumentState[]))]
    [JsonSourceGenerationOptions(WriteIndented = true, RespectNullableAnnotations = true)]
    internal partial class PinnedImageJsonContext : JsonSerializerContext;
}

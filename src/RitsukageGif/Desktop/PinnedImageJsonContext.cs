using System.Text.Json.Serialization;

namespace RitsukageGif.Desktop
{
    [JsonSerializable(typeof(PinnedImageState[]))]
    [JsonSerializable(typeof(PinnedDocumentState[]))]
    [JsonSourceGenerationOptions(WriteIndented = true)]
    internal partial class PinnedImageJsonContext : JsonSerializerContext;
}

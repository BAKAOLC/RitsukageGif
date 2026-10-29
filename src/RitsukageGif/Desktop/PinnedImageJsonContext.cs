using System.Text.Json.Serialization;

namespace RitsukageGif.Desktop
{
    [JsonSerializable(typeof(PinnedImageState[]))]
    [JsonSourceGenerationOptions(WriteIndented = true)]
    internal partial class PinnedImageJsonContext : JsonSerializerContext;
}

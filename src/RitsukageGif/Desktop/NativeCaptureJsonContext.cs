using System.Text.Json.Serialization;

namespace RitsukageGif.Desktop
{
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        GenerationMode = JsonSourceGenerationMode.Metadata)]
    [JsonSerializable(typeof(NativeCaptureMetadata))]
    [JsonSerializable(typeof(NativeCaptureGeometry[]))]
    internal sealed partial class NativeCaptureJsonContext : JsonSerializerContext;
}

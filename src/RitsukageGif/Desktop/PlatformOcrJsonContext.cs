using System.Text.Json.Serialization;

namespace RitsukageGif.Desktop
{
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(MacVisionOcrCapabilities))]
    [JsonSerializable(typeof(OcrRecognitionResult))]
    internal partial class PlatformOcrJsonContext : JsonSerializerContext;
}

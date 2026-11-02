using System.Text.Json.Serialization;

namespace RitsukageGif.Desktop
{
    [JsonSerializable(typeof(CaptureHistoryManifest))]
    [JsonSourceGenerationOptions(WriteIndented = false, RespectNullableAnnotations = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 32)]
    internal partial class CaptureHistoryJsonContext : JsonSerializerContext;
}

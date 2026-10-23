using System.Text.Json.Serialization;

namespace RitsukageGif.Desktop
{
    [JsonSerializable(typeof(AppSettings))]
    [JsonSourceGenerationOptions(WriteIndented = true, RespectNullableAnnotations = true)]
    internal partial class SettingsJsonContext : JsonSerializerContext;
}

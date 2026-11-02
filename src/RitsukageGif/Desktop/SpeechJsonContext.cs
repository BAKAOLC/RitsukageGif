using System.Text.Json.Serialization;

namespace RitsukageGif.Desktop
{
    [JsonSerializable(typeof(SpeechCapabilities))]
    [JsonSerializable(typeof(SpeechRequest))]
    internal partial class SpeechJsonContext : JsonSerializerContext;
}

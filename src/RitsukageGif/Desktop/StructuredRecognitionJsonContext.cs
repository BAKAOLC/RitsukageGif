using System.Text.Json.Serialization;

namespace RitsukageGif.Desktop
{
    [JsonSerializable(typeof(string[]))]
    [JsonSerializable(typeof(StructuredRecognitionRequest))]
    [JsonSerializable(typeof(StructuredRecognitionReply))]
    internal partial class StructuredRecognitionJsonContext : JsonSerializerContext;
}

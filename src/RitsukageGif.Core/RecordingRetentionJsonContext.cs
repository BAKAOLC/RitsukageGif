using System.Text.Json.Serialization;

namespace RitsukageGif.Core
{
    [JsonSerializable(typeof(RecordingRetentionIndex))]
    internal partial class RecordingRetentionJsonContext : JsonSerializerContext;
}

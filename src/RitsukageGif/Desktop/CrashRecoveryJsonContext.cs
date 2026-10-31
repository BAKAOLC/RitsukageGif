using System.Text.Json.Serialization;

namespace RitsukageGif.Desktop
{
    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(CrashRecoveryReport))]
    internal partial class CrashRecoveryJsonContext : JsonSerializerContext;
}

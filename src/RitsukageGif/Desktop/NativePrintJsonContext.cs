using System.Text.Json.Serialization;

namespace RitsukageGif.Desktop
{
    [JsonSerializable(typeof(PrintPageGeometry))]
    [JsonSerializable(typeof(PrintCapabilities))]
    internal partial class NativePrintJsonContext : JsonSerializerContext;
}

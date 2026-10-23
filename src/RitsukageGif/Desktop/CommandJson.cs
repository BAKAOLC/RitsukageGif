using System.Text.Json.Serialization;

namespace RitsukageGif.Desktop
{
    [JsonSerializable(typeof(string[]))]
    [JsonSerializable(typeof(CommandReply))]
    internal partial class CommandJson : JsonSerializerContext;
}

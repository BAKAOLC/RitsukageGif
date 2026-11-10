using System.Text.Json.Serialization;

namespace RitsukageGif.Desktop
{
    [JsonSerializable(typeof(string[]))]
    [JsonSerializable(typeof(CommandReply))]
    [JsonSerializable(typeof(CommandActionInfo[]))]
    [JsonSerializable(typeof(CommandOperationInfo[]))]
    internal partial class CommandJson : JsonSerializerContext;
}

using System.Text.Json.Serialization;

namespace RitsukageGif.Desktop
{
    [JsonSerializable(typeof(AnimationEditTransfer))]
    internal partial class AnimationImportJsonContext : JsonSerializerContext;
}

using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed class AnnotationColorJsonConverter : JsonConverter<Color>
    {
        public override Color Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String || !Color.TryParse(reader.GetString(), out var color))
                throw new JsonException("Invalid annotation color.");
            return color;
        }

        public override void Write(Utf8JsonWriter writer, Color value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }
}

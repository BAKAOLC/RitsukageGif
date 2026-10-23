using System.Text.Json;

namespace RitsukageGif.Scripting
{
    public static partial class DataScriptLibrary
    {
        internal static object? ParseJson(string text)
        {
            CheckSize(Utf8.GetByteCount(text));
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 64 });
            var remaining = MaximumItems;
            return ConvertJson(document.RootElement, ref remaining);
        }

        private static object? ConvertJson(JsonElement element, ref int remaining)
        {
            if (--remaining < 0) throw new ArgumentException(UiText.Get("ServiceLimitExceeded"));
            switch (element.ValueKind)
            {
                case JsonValueKind.Null: return null;
                case JsonValueKind.True: return true;
                case JsonValueKind.False: return false;
                case JsonValueKind.String: return element.GetString();
                case JsonValueKind.Number: return Finite(element.GetDouble());
                case JsonValueKind.Array:
                    var array = new List<object?>();
                    foreach (var item in element.EnumerateArray()) array.Add(ConvertJson(item, ref remaining));
                    return array;
                case JsonValueKind.Object:
                    var map = new Dictionary<string, object?>(StringComparer.Ordinal);
                    foreach (var item in element.EnumerateObject())
                        map.Add(item.Name, ConvertJson(item.Value, ref remaining));
                    return map;
                default: throw new ArgumentException(UiText.Get("ScriptTypeError"));
            }
        }

        private static double Finite(double value) => double.IsFinite(value)
            ? value
            : throw new ArgumentException(UiText.Get("ScriptTypeError"));

        private static string StringifyJson(object? value, bool pretty)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = pretty, MaxDepth = 64 }))
            {
                var remaining = MaximumItems;
                WriteJson(writer, value, new HashSet<object>(ReferenceEqualityComparer.Instance), 0, ref remaining);
                writer.Flush();
            }

            CheckSize(stream.Length);
            return Utf8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
        }

        private static void WriteJson(Utf8JsonWriter writer, object? value, HashSet<object> ancestors, int depth,
            ref int remaining)
        {
            if (depth > 64 || --remaining < 0) throw new ArgumentException(UiText.Get("ServiceLimitExceeded"));
            switch (value)
            {
                case null: writer.WriteNullValue(); break;
                case bool boolean: writer.WriteBooleanValue(boolean); break;
                case string text:
                    CheckSize(Utf8.GetByteCount(text));
                    writer.WriteStringValue(text);
                    break;
                case double number: writer.WriteNumberValue(Finite(number)); break;
                case int number: writer.WriteNumberValue(number); break;
                case long number: writer.WriteNumberValue(number); break;
                case List<object?> array:
                    if (!ancestors.Add(array)) throw new ArgumentException(UiText.Get("ScriptTypeError"));
                    writer.WriteStartArray();
                    foreach (var item in array) WriteJson(writer, item, ancestors, depth + 1, ref remaining);
                    writer.WriteEndArray();
                    ancestors.Remove(array);
                    break;
                case Dictionary<string, object?> map:
                    if (!ancestors.Add(map)) throw new ArgumentException(UiText.Get("ScriptTypeError"));
                    writer.WriteStartObject();
                    foreach (var (key, item) in map)
                    {
                        CheckSize(Utf8.GetByteCount(key));
                        writer.WritePropertyName(key);
                        WriteJson(writer, item, ancestors, depth + 1, ref remaining);
                    }

                    writer.WriteEndObject();
                    ancestors.Remove(map);
                    break;
                default: throw new ArgumentException(UiText.Get("ScriptTypeError"));
            }

            CheckSize(writer.BytesCommitted + writer.BytesPending);
        }
    }
}

using System.Globalization;
using System.Text;
using System.Text.Json;

namespace RitsukageGif.Scripting
{
    public static class ScriptValueJson
    {
        public static string Serialize(object? value, int maximumBytes = 1024 * 1024)
        {
            return Encoding.UTF8.GetString(Encode(value, maximumBytes).Span);
        }

        public static JsonElement ToElement(object? value, int maximumBytes = 256 * 1024)
        {
            using var document = JsonDocument.Parse(Encode(value, maximumBytes));
            return document.RootElement.Clone();
        }

        public static JsonElement Error(ScriptValueSerializationException error)
        {
            return ToElement(new Dictionary<string, object?>
            {
                ["error"] = new Dictionary<string, object?> { ["code"] = error.Code, ["message"] = error.Message },
            });
        }

        internal static ScriptValueSerializationException Limit()
        {
            return new("tooLarge", "The script value exceeds the JSON result limit.");
        }

        private static ReadOnlyMemory<byte> Encode(object? value, int maximumBytes)
        {
            if (maximumBytes is < 256 or > 16 * 1024 * 1024)
                throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            var buffer = new ScriptValueJsonBuffer(maximumBytes);
            using var writer = new Utf8JsonWriter(buffer, new() { MaxDepth = 64 });
            var remaining = 100000;
            Write(writer, value, new(ReferenceEqualityComparer.Instance), 0, ref remaining, maximumBytes);
            writer.Flush();
            return buffer.Written;
        }

        private static void Write(Utf8JsonWriter writer, object? value, HashSet<object> ancestors,
            int depth, ref int remaining, int maximumBytes)
        {
            if (depth >= 64 || writer.CurrentDepth >= 63 || --remaining < 0) throw Limit();
            var container = value is List<object?> or Dictionary<string, object?> or ScriptResult or ScriptOption
                or ScriptMap or ScriptSet;
            if (container && !ancestors.Add(value!))
                throw new ScriptValueSerializationException("cycle",
                    "Cyclic script values cannot be returned as JSON.");
            try
            {
                switch (value)
                {
                    case null: writer.WriteNullValue(); break;
                    case bool boolean: writer.WriteBooleanValue(boolean); break;
                    case string text:
                        if (Encoding.UTF8.GetByteCount(text) > maximumBytes) throw Limit();
                        writer.WriteStringValue(text);
                        break;
                    case double number when double.IsFinite(number): writer.WriteNumberValue(number); break;
                    case float number when float.IsFinite(number): writer.WriteNumberValue(number); break;
                    case decimal number: writer.WriteNumberValue(number); break;
                    case byte number: writer.WriteNumberValue(number); break;
                    case sbyte number: writer.WriteNumberValue(number); break;
                    case short number: writer.WriteNumberValue(number); break;
                    case ushort number: writer.WriteNumberValue(number); break;
                    case int number: writer.WriteNumberValue(number); break;
                    case uint number: writer.WriteNumberValue(number); break;
                    case long number: writer.WriteNumberValue(number); break;
                    case ulong number: writer.WriteNumberValue(number); break;
                    case Enum enumeration:
                        if (((IConvertible)enumeration).GetTypeCode() == TypeCode.UInt64)
                            writer.WriteNumberValue(Convert.ToUInt64(enumeration, CultureInfo.InvariantCulture));
                        else writer.WriteNumberValue(Convert.ToInt64(enumeration, CultureInfo.InvariantCulture));
                        break;
                    case ScriptBytes bytes:
                        if (bytes.Length > maximumBytes / 4 * 3) throw Limit();
                        writer.WriteStartObject();
                        writer.WriteString("type", "bytes");
                        writer.WriteBase64String("base64", bytes.Data.Span);
                        writer.WriteEndObject();
                        break;
                    case List<object?> items:
                        writer.WriteStartArray();
                        foreach (var item in items)
                            Write(writer, item, ancestors, depth + 1, ref remaining, maximumBytes);
                        writer.WriteEndArray();
                        break;
                    case Dictionary<string, object?> fields:
                        writer.WriteStartObject();
                        foreach (var (name, item) in fields)
                        {
                            if (Encoding.UTF8.GetByteCount(name) > maximumBytes) throw Limit();
                            writer.WritePropertyName(name);
                            Write(writer, item, ancestors, depth + 1, ref remaining, maximumBytes);
                        }

                        writer.WriteEndObject();
                        break;
                    case ScriptResult result:
                        writer.WriteStartObject();
                        writer.WriteString("tag", result.IsOk ? "ok" : "err");
                        writer.WritePropertyName(result.IsOk ? "value" : "error");
                        Write(writer, result.Value, ancestors, depth + 1, ref remaining, maximumBytes);
                        writer.WriteEndObject();
                        break;
                    case ScriptOption option:
                        writer.WriteStartObject();
                        writer.WriteString("tag", option.HasValue ? "some" : "none");
                        if (option.HasValue)
                        {
                            writer.WritePropertyName("value");
                            Write(writer, option.Value, ancestors, depth + 1, ref remaining, maximumBytes);
                        }

                        writer.WriteEndObject();
                        break;
                    case ScriptMap map:
                        writer.WriteStartObject();
                        writer.WriteString("type", "map");
                        writer.WritePropertyName("entries");
                        Write(writer, map.Entries(), ancestors, depth + 1, ref remaining, maximumBytes);
                        writer.WriteEndObject();
                        break;
                    case ScriptSet set:
                        writer.WriteStartObject();
                        writer.WriteString("type", "set");
                        writer.WritePropertyName("values");
                        Write(writer, set.Values.ToList(), ancestors, depth + 1, ref remaining, maximumBytes);
                        writer.WriteEndObject();
                        break;
                    case double or float:
                        throw new ScriptValueSerializationException("nonFinite",
                            "Non-finite numbers cannot be returned as JSON.");
                    default:
                        throw new ScriptValueSerializationException("unsupported",
                            "A live host handle or function cannot be returned as JSON. Return its data or save it to a file first.");
                }
            }
            finally
            {
                if (container) ancestors.Remove(value!);
            }
        }
    }
}

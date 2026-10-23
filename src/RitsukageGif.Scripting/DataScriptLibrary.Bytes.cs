using System.Security.Cryptography;

namespace RitsukageGif.Scripting
{
    public static partial class DataScriptLibrary
    {
        private static void RegisterBytes(ScriptBindingRegistry bindings)
        {
            var type = bindings.RegisterType<ScriptBytes>("Bytes");
            type.Property("length", "number", value => value.Length);
            type.Method("text",
                (value, _, _) => ValueTask.FromResult<object?>(Try(() => Utf8.GetString(value.Data.Span))),
                returnType: "Result<string,ServiceError>");
            type.Method("toBase64",
                (value, _, _) => ValueTask.FromResult<object?>(Convert.ToBase64String(value.Data.Span)),
                returnType: "string");
            type.Method("toHex",
                (value, _, _) => ValueTask.FromResult<object?>(Convert.ToHexStringLower(value.Data.Span)),
                returnType: "string");
            type.Method("sha256",
                (value, _, _) =>
                    ValueTask.FromResult<object?>(Convert.ToHexStringLower(SHA256.HashData(value.Data.Span))),
                returnType: "string");
            type.Method("slice", (value, args, _) => ValueTask.FromResult<object?>(Try(() =>
            {
                var start = ByteIndex(args[0], value.Length);
                var end = args[1] is null ? value.Length : ByteIndex(args[1], value.Length);
                if (end < start) throw new ArgumentOutOfRangeException(nameof(args));
                return new ScriptBytes(value.Data.Slice(start, end - start));
            })), [new("start", "number"), new("end", "number?", true)], "Result<Bytes,ServiceError>");
            var table = bindings.RegisterTable("bytes");
            table.Function("fromText", Wrap(args =>
            {
                var text = (string)args[0]!;
                CheckSize(Utf8.GetByteCount(text));
                return new ScriptBytes(Utf8.GetBytes(text));
            }), [new("text", "string")], "Result<Bytes,ServiceError>");
            table.Function("fromBase64", Wrap(args =>
            {
                var text = (string)args[0]!;
                if (text.Length > (MaximumBytes + 2L) / 3 * 4)
                    throw new ArgumentException(UiText.Get("ServiceLimitExceeded"));
                var data = Convert.FromBase64String(text);
                CheckSize(data.Length);
                return new ScriptBytes(data);
            }), [new("text", "string")], "Result<Bytes,ServiceError>");
            table.Function("fromHex", Wrap(args =>
            {
                var text = (string)args[0]!;
                CheckSize(text.Length / 2L);
                return new ScriptBytes(Convert.FromHexString(text));
            }), [new("text", "string")], "Result<Bytes,ServiceError>");
        }

        private static int ByteIndex(object? value, int length)
        {
            var number = ScriptEngine.Number(value);
            if (!double.IsFinite(number) || !number.Equals(Math.Truncate(number)) || number < -length ||
                number > length)
                throw new ArgumentOutOfRangeException(nameof(value));
            return number < 0 ? length + (int)number : (int)number;
        }
    }
}

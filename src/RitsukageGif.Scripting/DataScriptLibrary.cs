using System.Text;
using System.Text.Json;

namespace RitsukageGif.Scripting
{
    public static partial class DataScriptLibrary
    {
        internal const int MaximumBytes = 16 * 1024 * 1024;
        private const int MaximumItems = 100000;
        private static readonly UTF8Encoding Utf8 = new(false, true);

        public static void Register(ScriptBindingRegistry bindings)
        {
            ScriptServiceErrors.Register(bindings);
            var json = bindings.RegisterTable("json");
            json.Function("parse", Wrap(args => ParseJson((string)args[0]!)),
                [new("text", "string")], "Result<any,ServiceError>");
            json.Function("stringify", Wrap(args => StringifyJson(args[0], (bool)args[1]!)),
                [new("value"), new("pretty", "boolean", true, false)], "Result<string,ServiceError>");
            var csv = bindings.RegisterTable("csv");
            csv.Function("parse", Wrap(args => ParseCsv((string)args[0]!, Delimiter(args[1]))),
                [new("text", "string"), new("delimiter", "string", true, ",")], "Result<string[][],ServiceError>");
            csv.Function("stringify", Wrap(args => StringifyCsv((List<object?>)args[0]!, Delimiter(args[1]))),
                [new("rows", "string[][]"), new("delimiter", "string", true, ",")], "Result<string,ServiceError>");
            RegisterBytes(bindings);
        }

        private static ScriptNative Wrap(Func<IReadOnlyList<object?>, object?> action)
        {
            return (args, token) =>
            {
                token.ThrowIfCancellationRequested();
                var result = Try(() => action(args));
                token.ThrowIfCancellationRequested();
                return ValueTask.FromResult<object?>(result);
            };
        }

        private static ScriptResult Try(Func<object?> action)
        {
            try
            {
                return new(true, action());
            }
            catch (Exception exception) when (exception is ArgumentException or FormatException or JsonException
                                                  or InvalidOperationException)
            {
                return new(false, ScriptServiceErrors.Create("invalidData", exception.Message));
            }
        }

        private static void CheckSize(long size)
        {
            if (size > MaximumBytes) throw new ArgumentException(UiText.Get("ServiceLimitExceeded"));
        }

        private static char Delimiter(object? value)
        {
            if (value is not string { Length: 1 } text || text[0] is '\r' or '\n' or '"' or '\0' ||
                char.IsSurrogate(text[0]))
                throw new ArgumentException(UiText.Get("ScriptTypeError"));
            return text[0];
        }
    }
}

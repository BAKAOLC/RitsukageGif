namespace RitsukageGif.Scripting
{
    public static partial class StandardScriptLibrary
    {
        private static void RegisterStrings(ScriptBindingRegistry bindings)
        {
            var strings = bindings.RegisterType<string>("string");
            strings.Property("length", "number", text => text.Length);
            strings.Method("contains",
                (text, args, _) => Result(text.Contains((string)args[0]!, StringComparison.Ordinal)),
                [new("value", "string")], "boolean");
            strings.Method("includes",
                (text, args, _) => Result(text.Contains((string)args[0]!, StringComparison.Ordinal)),
                [new("value", "string")], "boolean");
            strings.Method("startsWith",
                (text, args, _) => Result(text.StartsWith((string)args[0]!, StringComparison.Ordinal)),
                [new("value", "string")], "boolean");
            strings.Method("endsWith",
                (text, args, _) => Result(text.EndsWith((string)args[0]!, StringComparison.Ordinal)),
                [new("value", "string")], "boolean");
            strings.Method("indexOf",
                (text, args, _) => Result(text.IndexOf((string)args[0]!, Offset(args[1], text.Length, false),
                    StringComparison.Ordinal)),
                [new("value", "string"), new("start", "number", true, 0d)], "number");
            strings.Method("split", (text, args, _) => Result((string)args[0]! == ""
                    ? text.Select(character => (object?)character.ToString()).ToList()
                    : text.Split((string)args[0]!).Cast<object?>().ToList()),
                [new("separator", "string")], "array");
            strings.Method("slice", (text, args, _) =>
            {
                var start = Offset(args[0], text.Length);
                var end = args[1] is null ? text.Length : Offset(args[1], text.Length);
                return Result(text.Substring(start, Math.Max(0, end - start)));
            }, [new("start", "number", true, 0d), new("end", "number", true)], "string");
            strings.Method("substring", (text, args, _) =>
            {
                var start = Offset(args[0], text.Length, false);
                var end = args[1] is null ? text.Length : Offset(args[1], text.Length, false);
                return Result(text.Substring(Math.Min(start, end), Math.Abs(end - start)));
            }, [new("start", "number", true, 0d), new("end", "number", true)], "string");
            strings.Method("replace", (text, args, _) =>
            {
                var old = (string)args[0]!;
                var at = text.IndexOf(old, StringComparison.Ordinal);
                return Result(at < 0 ? text : text[..at] + (string)args[1]! + text[(at + old.Length)..]);
            }, [new("search", "string"), new("replacement", "string")], "string");
            strings.Method("replaceAll", (text, args, _) =>
            {
                var old = (string)args[0]!;
                var replacement = (string)args[1]!;
                return Result(old.Length == 0
                    ? replacement + string.Join(replacement, text.Select(character => character.ToString())) +
                      (text.Length == 0 ? "" : replacement)
                    : text.Replace(old, replacement, StringComparison.Ordinal));
            }, [new("search", "string"), new("replacement", "string")], "string");
            strings.Method("repeat", (text, args, _) =>
            {
                var count = Index(args[0]);
                if ((long)text.Length * count > 16 * 1024 * 1024)
                    throw new InvalidOperationException(UiText.Get("DslExecutionLimit"));
                return Result(text.Length == 0 ? "" : string.Concat(Enumerable.Repeat(text, count)));
            }, [new("count", "number")], "string");
            strings.Method("trim", (text, _, _) => Result(text.Trim()), returnType: "string");
            strings.Method("trimStart", (text, _, _) => Result(text.TrimStart()), returnType: "string");
            strings.Method("trimEnd", (text, _, _) => Result(text.TrimEnd()), returnType: "string");
            strings.Method("lower", (text, _, _) => Result(text.ToLowerInvariant()), returnType: "string");
            strings.Method("upper", (text, _, _) => Result(text.ToUpperInvariant()), returnType: "string");
            strings.Indexer((text, key) =>
            {
                var index = Index(key);
                return index < text.Length
                    ? text[index].ToString()
                    : throw new IndexOutOfRangeException(UiText.Get("ScriptIndex"));
            });
        }
    }
}

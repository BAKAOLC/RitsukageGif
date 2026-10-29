namespace RitsukageGif.Scripting
{
    public static partial class StandardScriptLibrary
    {
        public static ScriptBindingRegistry CreateBindings(PresetDebugger? debugger = null)
        {
            var bindings = new ScriptBindingRegistry();
            bindings.RegisterFunction("len", ScriptEngine.Native(args => args[0] switch
            {
                string text => text.Length, List<object?> array => array.Count,
                Dictionary<string, object?> map => map.Count, ScriptMap map => map.Count,
                ScriptSet set => set.Count, _ => throw TypeError(),
            }), [new("value")], "number");
            bindings.RegisterFunction("str", ScriptEngine.Native(args => Text(args[0])),
                [new("value")], "string");
            bindings.RegisterFunction("number", ScriptEngine.Native(args =>
                ParseNumber(args[0])), [new("value")], "number");
            bindings.RegisterFunction("assert", ScriptEngine.Native(args => ScriptEngine.Truth(args[0])
                    ? null
                    : throw new InvalidOperationException(args[1] is null
                        ? UiText.Get("DslAssertion")
                        : Text(args[1]))),
                [new("condition", "boolean"), new("message", Optional: true)], "void");
            var output = ScriptEngine.Native(args =>
            {
                debugger?.Log(string.Join(" ", args.Select(Text)));
                return null;
            });
            bindings.RegisterFunction("print", output, [new("values", Variadic: true)], "void");
            bindings.RegisterFunction("log", output, [new("values", Variadic: true)], "void");

            RegisterMath(bindings);
            RegisterStrings(bindings);
            RegisterArrays(bindings);
            RegisterObjects(bindings);
            RegisterCollections(bindings);
            RegisterResults(bindings);
            DataScriptLibrary.Register(bindings);
            return bindings;
        }

        private static void AddUnary(ScriptTableBinding table, string name, Func<double, double> function)
        {
            table.Function(name, ScriptEngine.Native(args => function(ScriptEngine.Number(args[0]))),
                [new("value", "number")], "number");
        }

        private static void AddBinary(ScriptTableBinding table, string name, Func<double, double, double> function)
        {
            table.Function(name,
                ScriptEngine.Native(args => function(ScriptEngine.Number(args[0]), ScriptEngine.Number(args[1]))),
                [new("left", "number"), new("right", "number")], "number");
        }

        private static ValueTask<object?> Result(object? value)
        {
            return ValueTask.FromResult(value);
        }

        private static string Text(object? value)
        {
            return ScriptValueText.Convert(value);
        }

        private static InvalidOperationException TypeError()
        {
            return new(UiText.Get("ScriptTypeError"));
        }

        private static int Index(object? key)
        {
            var number = ScriptEngine.Number(key);
            return number is >= 0 and <= int.MaxValue && number.Equals(Math.Truncate(number))
                ? (int)number
                : throw TypeError();
        }
    }
}

namespace RitsukageGif.Scripting
{
    public static class ParameterScriptLibrary
    {
        public static void Register(ScriptBindingRegistry bindings, ScriptParameters? parameters = null,
            Func<double, CancellationToken, ValueTask<double>>? wait = null)
        {
            var values = parameters ?? new ScriptParameters();
            bindings.RegisterObjectSchema("ParameterState", [new("revision", "number"), new("values", "object")]);
            var table = bindings.RegisterTable("params");
            table.Function("state", ScriptEngine.Native(_ => values.State()), [], "ParameterState");
            table.Function("waitChanged",
                async (args, token) =>
                    wait is null
                        ? await values.WaitChangedAsync((double)args[0]!, token)
                        : await wait((double)args[0]!, token),
                [new("revision", "number")], "number");
            table.Function("get", ScriptEngine.Native(args => values.Get((string)args[0]!, args[1])),
                [new("name", "string"), new("fallback", "any", true)]);
            table.Function("number",
                ScriptEngine.Native(args => values.Get((string)args[0]!) is double number ? number : args[1]),
                [new("name", "string"), new("fallback", "number", true, 0d)], "number");
            table.Function("string",
                ScriptEngine.Native(args => values.Get((string)args[0]!) as string ?? args[1]),
                [new("name", "string"), new("fallback", "string", true, "")], "string");
            table.Function("boolean",
                ScriptEngine.Native(args => values.Get((string)args[0]!) is bool value ? value : args[1]),
                [new("name", "string"), new("fallback", "boolean", true, false)], "boolean");
            table.Function("snapshot", ScriptEngine.Native(_ => values.Snapshot()), [], "object");
            table.Function("revision", ScriptEngine.Native(_ => (double)values.Revision), [], "number");
        }
    }
}

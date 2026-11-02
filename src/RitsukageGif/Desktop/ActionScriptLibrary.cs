using System.Globalization;

namespace RitsukageGif.Desktop
{
    internal static class ActionScriptLibrary
    {
        internal static ScriptBindingRegistry CreateBindings()
        {
            var bindings = CaptureScriptLibrary.CreateBindings();
            Register(bindings);
            return bindings;
        }

        internal static void Register(ScriptBindingRegistry bindings,
            Func<GlobalActionBinding, CancellationToken, Task>? execute = null)
        {
            bindings.RegisterEnum("ActionKind", GlobalActions.Catalog
                .Where(definition => definition.Kind != GlobalActionKind.RunScript)
                .ToDictionary(definition => definition.Kind.ToString(), definition => definition.Kind.ToString()));
            bindings.RegisterObjectSchema("ActionOptions",
            [
                new("parameter", "string", true), new("preset", "string", true),
                new("arguments", "object", true),
            ]);
            bindings.RegisterTable("actions").Function("run", async (arguments, token) =>
            {
                token.ThrowIfCancellationRequested();
                if (execute is null) throw new InvalidOperationException(UiText.Get("CommandNotReady"));
                var action = Read(arguments);
                try
                {
                    await execute(action, token);
                    return new ScriptResult(true, null);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException
                                                  or UnauthorizedAccessException or NotSupportedException)
                {
                    return new ScriptResult(false, error.Message);
                }
            }, [new("kind", "ActionKind"), new("options", "ActionOptions", true)], "Result<void, string>");
        }

        private static GlobalActionBinding Read(IReadOnlyList<object?> arguments)
        {
            if (arguments[0] is not string name || !Enum.TryParse<GlobalActionKind>(name, out var kind) ||
                !Enum.IsDefined(kind) || kind == GlobalActionKind.RunScript)
                throw new ArgumentException(UiText.Get("GlobalActionInvalidKind"));
            var fields = arguments.Count > 1 && arguments[1] is not null
                ? arguments[1] as Dictionary<string, object?> ??
                  throw new ArgumentException(UiText.Get("ScriptTypeError"))
                : [];
            var action = new GlobalActionBinding { Id = "script-call", Kind = kind, ShowInTray = false };
            foreach (var (key, value) in fields)
                switch (key)
                {
                    case "parameter" when value is string parameter: action.Parameter = parameter; break;
                    case "preset" when value is string preset: action.PresetName = preset; break;
                    case "arguments" when value is Dictionary<string, object?> values:
                        foreach (var (argument, item) in values)
                            action.Arguments.Add(argument, item switch
                            {
                                string text => text,
                                bool boolean => boolean ? "true" : "false",
                                double number when double.IsFinite(number) => number.ToString("R",
                                    CultureInfo.InvariantCulture),
                                _ => throw new ArgumentException(UiText.Get("ScriptTypeError")),
                            });
                        break;
                    default: throw new ArgumentException(UiText.Get("ScriptTypeError"));
                }

            var errors = GlobalActions.Validate([action], []);
            if (errors.Count != 0) throw new ArgumentException(string.Join(Environment.NewLine, errors));
            return action;
        }
    }
}

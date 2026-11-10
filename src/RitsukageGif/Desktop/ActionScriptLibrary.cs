using System.Globalization;

namespace RitsukageGif.Desktop
{
    internal static class ActionScriptLibrary
    {
        internal static ScriptBindingRegistry CreateBindings()
        {
            return AutomationScriptLibrary.CreateBindings();
        }

        internal static void Register(ScriptBindingRegistry bindings,
            Func<GlobalActionBinding, CancellationToken, Task<ScreenshotInvocation?>>? execute = null,
            Func<IReadOnlyList<string>, CancellationToken, Task>? pinFiles = null,
            Func<IReadOnlyList<GlobalActionBinding>>? savedActions = null)
        {
            bindings.RegisterEnum("ActionKind", GlobalActions.Catalog
                .Where(definition => definition.Kind != GlobalActionKind.RunScript)
                .ToDictionary(definition => definition.Kind.ToString(), definition => definition.Kind.ToString()));
            bindings.RegisterObjectSchema("ActionOptions",
            [
                new("parameter", "string", true), new("preset", "string", true),
                new("arguments", "object", true),
            ]);
            bindings.RegisterObjectSchema("ActionInfo",
            [
                new("id", "string?", true), new("kind", "string"), new("name", "string"),
                new("enabled", "boolean"), new("fileParameter", "boolean"),
            ]);
            if (bindings.GetObjectSchema("Rectangle") is null)
                bindings.RegisterObjectSchema("Rectangle", [
                    new("x", "number"), new("y", "number"),
                    new("width", "number"), new("height", "number"),
                ]);
            bindings.RegisterObjectSchema("ActionResult", [new("paths", "string[]"), new("region", "Rectangle?")]);
            var actions = bindings.RegisterTable("actions");
            actions.Function("list", (_, token) =>
            {
                token.ThrowIfCancellationRequested();
                var catalog =
                    GlobalActions.Catalog.Select(definition => Describe(new() { Id = "", Kind = definition.Kind }));
                var saved = savedActions?.Invoke().Select(Describe) ?? [];
                return ValueTask.FromResult<object?>(catalog.Concat(saved).Cast<object?>().ToList());
            }, [], "ActionInfo[]");
            actions.Function("get", (arguments, token) =>
            {
                token.ThrowIfCancellationRequested();
                var action = savedActions?.Invoke().SingleOrDefault(item => item.Id == (string)arguments[0]!);
                return ValueTask.FromResult<object?>(action is null
                    ? new ScriptResult(false, "Action does not exist.")
                    : new ScriptResult(true, Describe(action)));
            }, [new("id", "string")], "Result<ActionInfo, string>");
            actions.Function("runId", async (arguments, token) =>
            {
                if (execute is null) throw new InvalidOperationException(UiText.Get("CommandNotReady"));
                var action = savedActions?.Invoke().SingleOrDefault(item => item.Id == (string)arguments[0]!);
                if (action is null) return new ScriptResult(false, "Action does not exist.");
                try
                {
                    return new ScriptResult(true, ActionResult(await execute(action, token)));
                }
                catch (OperationCanceledException error) when (token.IsCancellationRequested ||
                                                               error.CancellationToken.IsCancellationRequested)
                {
                    token.ThrowIfCancellationRequested();
                    return new ScriptResult(false, UiText.Get("Cancelled"));
                }
                catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException
                                                  or UnauthorizedAccessException or NotSupportedException)
                {
                    return new ScriptResult(false, error.Message);
                }
            }, [new("id", "string")], "Result<ActionResult,string>");
            actions.Function("run", async (arguments, token) =>
            {
                token.ThrowIfCancellationRequested();
                if (execute is null) throw new InvalidOperationException(UiText.Get("CommandNotReady"));
                var action = Read(arguments);
                try
                {
                    return new ScriptResult(true, ActionResult(await execute(action, token)));
                }
                catch (OperationCanceledException error) when (token.IsCancellationRequested ||
                                                               error.CancellationToken.IsCancellationRequested)
                {
                    token.ThrowIfCancellationRequested();
                    return new ScriptResult(false, UiText.Get("Cancelled"));
                }
                catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException
                                                  or UnauthorizedAccessException or NotSupportedException)
                {
                    return new ScriptResult(false, error.Message);
                }
            }, [new("kind", "ActionKind"), new("options", "ActionOptions", true)], "Result<ActionResult,string>");
            actions.Function("pinFiles", async (arguments, token) =>
            {
                token.ThrowIfCancellationRequested();
                if (pinFiles is null) throw new InvalidOperationException(UiText.Get("CommandNotReady"));
                var paths = ((List<object?>)arguments[0]!).Select(value => (string)value!).ToArray();
                try
                {
                    await pinFiles(paths, token);
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
            }, [new("paths", "string[]")], "Result<void, string>");
        }

        private static Dictionary<string, object?> Describe(GlobalActionBinding action)
        {
            return new()
            {
                ["id"] = string.IsNullOrEmpty(action.Id) ? null : action.Id,
                ["kind"] = action.Kind.ToString(), ["name"] = GlobalActions.Label(action),
                ["enabled"] = action.Enabled,
                ["fileParameter"] = GlobalActions.Definition(action.Kind).FileParameter,
            };
        }

        private static Dictionary<string, object?> ActionResult(ScreenshotInvocation? invocation)
        {
            return new()
            {
                ["paths"] = invocation?.Paths.Cast<object?>().ToList() ?? [],
                ["region"] = invocation?.Region is { } region
                    ? new Dictionary<string, object?>
                    {
                        ["x"] = (double)region.X, ["y"] = (double)region.Y,
                        ["width"] = (double)region.Width, ["height"] = (double)region.Height,
                    }
                    : null,
            };
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

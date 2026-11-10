using System.Text.Json;

namespace RitsukageGif.Scripting
{
    public static class SettingsScriptLibrary
    {
        public static void Register(ScriptBindingRegistry bindings, ISettingsAutomationService? service = null,
            string? baseDirectory = null)
        {
            ScriptServiceErrors.Register(bindings);
            bindings.RegisterObjectSchema("SettingsApplyOptions", [new("proxyPassword", "string", true)]);
            var root = Path.GetFullPath(baseDirectory ?? Environment.CurrentDirectory);
            var table = bindings.RegisterTable("settings");
            table.Function("get", async (args, token) =>
            {
                try
                {
                    var json = await Required(service).GetAsync((string)args[0]!, token);
                    return new ScriptResult(true, DataScriptLibrary.ParseJson(json));
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    token.ThrowIfCancellationRequested();
                    return Failure(error);
                }
            }, [new("pointer", "string", true, "")], "Result<any,ServiceError>");
            table.Function("set", async (args, token) =>
                {
                    try
                    {
                        var json = ScriptValueJson.Serialize(args[1], 4 * 1024 * 1024);
                        await Required(service).SetAsync((string)args[0]!, json, Options(args[2]), token);
                        return new ScriptResult(true, null);
                    }
                    catch (Exception error) when (error is not OperationCanceledException)
                    {
                        token.ThrowIfCancellationRequested();
                        return Failure(error);
                    }
                }, [new("pointer", "string"), new("value"), new("options", "SettingsApplyOptions", true)],
                "Result<void,ServiceError>");
            table.Function("import", async (args, token) =>
            {
                try
                {
                    await Required(service)
                        .ImportAsync(Path.GetFullPath((string)args[0]!, root), Options(args[1]), token);
                    return new ScriptResult(true, null);
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    token.ThrowIfCancellationRequested();
                    return Failure(error);
                }
            }, [new("path", "string"), new("options", "SettingsApplyOptions", true)], "Result<void,ServiceError>");
            table.Function("export", async (args, token) =>
            {
                try
                {
                    var path = await Required(service).ExportAsync(Path.GetFullPath((string)args[0]!, root), token);
                    return new ScriptResult(true, path);
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    token.ThrowIfCancellationRequested();
                    return Failure(error);
                }
            }, [new("path", "string")], "Result<string,ServiceError>");
            table.Function("reset", async (args, token) =>
                {
                    try
                    {
                        await Required(service).ResetAsync((string)args[0]!, Options(args[1]), token);
                        return new ScriptResult(true, null);
                    }
                    catch (Exception error) when (error is not OperationCanceledException)
                    {
                        token.ThrowIfCancellationRequested();
                        return Failure(error);
                    }
                }, [new("pointer", "string", true, ""), new("options", "SettingsApplyOptions", true)],
                "Result<void,ServiceError>");
        }

        private static ISettingsAutomationService Required(ISettingsAutomationService? service)
        {
            return service ?? throw new InvalidOperationException(UiText.Get("EditorNoRuntime"));
        }

        private static SettingsAutomationOptions? Options(object? value)
        {
            if (value is null) return null;
            if (value is not Dictionary<string, object?> fields ||
                fields.Keys.Any(key => key != "proxyPassword") ||
                (fields.TryGetValue("proxyPassword", out var password) && password is not string))
                throw new ArgumentException(UiText.Get("ScriptTypeError"));
            return new() { ProxyPassword = fields.GetValueOrDefault("proxyPassword") as string };
        }

        private static ScriptResult Failure(Exception error)
        {
            return new(false, error switch
            {
                KeyNotFoundException => ScriptServiceErrors.Create("notFound", error.Message),
                JsonException => ScriptServiceErrors.Create("invalidData", error.Message),
                _ => ScriptServiceErrors.FromException(error),
            });
        }
    }
}

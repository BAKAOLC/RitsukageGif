namespace RitsukageGif.Scripting
{
    public static partial class FileScriptLibrary
    {
        private static void RegisterPaths(ScriptBindingRegistry bindings, Func<string, string> resolve)
        {
            var paths = bindings.RegisterTable("paths");
            ScriptParameter[] parameter = [new("path", "string")];
            paths.Function("join", Wrap(args => Path.Combine(args.Cast<string>().ToArray())),
                [new("parts", "string", Variadic: true)], "Result<string,ServiceError>");
            paths.Function("absolute", Wrap(args => resolve((string)args[0]!)), parameter,
                "Result<string,ServiceError>");
            paths.Function("directory", Wrap(args => Path.GetDirectoryName((string)args[0]!) ?? ""), parameter,
                "Result<string,ServiceError>");
            paths.Function("name", Wrap(args => Path.GetFileName((string)args[0]!)), parameter,
                "Result<string,ServiceError>");
            paths.Function("extension", Wrap(args => Path.GetExtension((string)args[0]!)), parameter,
                "Result<string,ServiceError>");
        }
    }
}

namespace RitsukageGif.Scripting
{
    public static class ScriptServiceErrors
    {
        public static void Register(ScriptBindingRegistry bindings)
        {
            if (bindings.GetObjectSchema("ServiceError") is not null) return;
            bindings.RegisterObjectSchema("ServiceError",
                [new("code", "string"), new("message", "string"), new("path", "string")]);
        }

        public static Dictionary<string, object?> Create(string code, string message, string? path = null) => new()
        {
            ["code"] = code, ["message"] = message, ["path"] = path ?? ""
        };

        public static Dictionary<string, object?> FromException(Exception exception, string? path = null) =>
            Create(exception switch
            {
                FileNotFoundException or DirectoryNotFoundException => "notFound",
                UnauthorizedAccessException => "accessDenied",
                PathTooLongException => "invalidPath",
                ArgumentException or NotSupportedException => "invalidArgument",
                IOException => "ioError",
                _ => "operationFailed"
            }, exception.Message, path);
    }
}

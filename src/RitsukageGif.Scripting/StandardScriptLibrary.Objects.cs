namespace RitsukageGif.Scripting
{
    public static partial class StandardScriptLibrary
    {
        private static void RegisterObjects(ScriptBindingRegistry bindings)
        {
            var objects = bindings.RegisterType<Dictionary<string, object?>>("object");
            objects.Method("keys", (map, _, _) => Result(map.Keys.Cast<object?>().ToList()), returnType: "array");
            objects.Method("values", (map, _, _) => Result(map.Values.ToList()), returnType: "array");
            objects.Method("entries",
                (map, _, _) =>
                    Result(map.Select(object? (pair) => new List<object?> { pair.Key, pair.Value }).ToList()),
                returnType: "array");
            objects.Method("has", (map, args, _) => Result(map.ContainsKey((string)args[0]!)), [new("key", "string")],
                "boolean");
            objects.Method("delete", (map, args, _) => Result(map.Remove((string)args[0]!)), [new("key", "string")],
                "boolean");
            objects.Indexer((map, key) => key is string name ? map.GetValueOrDefault(name) : throw TypeError(),
                (map, key, value) =>
                {
                    if (key is not string name) throw TypeError();
                    map[name] = value;
                }, (map, key) => key is string name && map.ContainsKey(name), true);
        }
    }
}

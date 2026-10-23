namespace RitsukageGif.Scripting
{
    public static partial class StandardScriptLibrary
    {
        private static void RegisterCollections(ScriptBindingRegistry bindings)
        {
            var maps = bindings.RegisterType<ScriptMap>("Map");
            bindings.RegisterFunction("Map", ScriptEngine.Native(args =>
            {
                var result = new ScriptMap();
                if (args[0] is List<object?> entries)
                    foreach (var item in entries)
                    {
                        if (item is not List<object?> { Count: 2 } pair) throw TypeError();
                        result.Set(pair[0], pair[1]);
                    }

                return result;
            }), [new("entries", "array", true)], "Map");
            maps.Property("size", "number", map => map.Count);
            maps.Method("get", (map, args, _) => Result(map.Get(args[0])), [new("key")]);
            maps.Method("set", (map, args, _) =>
            {
                map.Set(args[0], args[1]);
                return Result(map);
            }, [new("key"), new("value")], "Map");
            maps.Method("has", (map, args, _) => Result(map.Has(args[0])), [new("key")], "boolean");
            maps.Method("delete", (map, args, _) => Result(map.Delete(args[0])), [new("key")], "boolean");
            maps.Method("clear", (map, _, _) =>
            {
                map.Clear();
                return Result(null);
            }, returnType: "void");
            maps.Method("keys", (map, _, _) => Result(map.Keys.ToList()), returnType: "array");
            maps.Method("values", (map, _, _) => Result(map.Values.ToList()), returnType: "array");
            maps.Method("entries", (map, _, _) => Result(map.Entries()), returnType: "array");
            var sets = bindings.RegisterType<ScriptSet>("Set");
            bindings.RegisterFunction("Set", ScriptEngine.Native(args =>
            {
                var result = new ScriptSet();
                if (args[0] is List<object?> values)
                    foreach (var value in values)
                        result.Add(value);
                return result;
            }), [new("values", "array", true)], "Set");
            sets.Property("size", "number", set => set.Count);
            sets.Method("add", (set, args, _) =>
            {
                set.Add(args[0]);
                return Result(set);
            }, [new("value")], "Set");
            sets.Method("has", (set, args, _) => Result(set.Has(args[0])), [new("value")], "boolean");
            sets.Method("delete", (set, args, _) => Result(set.Delete(args[0])), [new("value")], "boolean");
            sets.Method("clear", (set, _, _) =>
            {
                set.Clear();
                return Result(null);
            }, returnType: "void");
            sets.Method("values", (set, _, _) => Result(set.Values.ToList()), returnType: "array");
        }
    }
}

namespace RitsukageGif.Scripting
{
    public static partial class StandardScriptLibrary
    {
        private static void RegisterArrayCallbacks(ScriptBindingRegistry bindings, ScriptHostType<List<object?>> arrays)
        {
            arrays.Method("map", async (array, args, token) =>
                {
                    var result = new List<object?>();
                    var length = array.Count;
                    for (var i = 0; i < length && i < array.Count; i++)
                        result.Add(await bindings.InvokeCallableAsync(args[0], [array[i], (double)i, array], token));
                    return result;
                }, [new("callback", "function", CallbackParameters: ["$element", "number", "$receiver"])], "array",
                inferredReturnType: "($callback0)[]");
            arrays.Method("filter", async (array, args, token) =>
                {
                    var result = new List<object?>();
                    var length = array.Count;
                    for (var i = 0; i < length && i < array.Count; i++)
                    {
                        var item = array[i];
                        if (ScriptEngine.Truth(
                                await bindings.InvokeCallableAsync(args[0], [item, (double)i, array], token)))
                            result.Add(item);
                    }

                    return result;
                },
                [
                    new("callback", "function", CallbackParameters: ["$element", "number", "$receiver"],
                        CallbackReturnType: "boolean")
                ], "array", inferredReturnType: "$receiver");
            arrays.Method("forEach", async (array, args, token) =>
            {
                var length = array.Count;
                for (var i = 0; i < length && i < array.Count; i++)
                    await bindings.InvokeCallableAsync(args[0], [array[i], (double)i, array], token);
                return null;
            }, [new("callback", "function", CallbackParameters: ["$element", "number", "$receiver"])], "void");
            arrays.Method("some", async (array, args, token) =>
                {
                    var length = array.Count;
                    for (var i = 0; i < length && i < array.Count; i++)
                        if (ScriptEngine.Truth(
                                await bindings.InvokeCallableAsync(args[0], [array[i], (double)i, array], token)))
                            return true;
                    return false;
                },
                [
                    new("callback", "function", CallbackParameters: ["$element", "number", "$receiver"],
                        CallbackReturnType: "boolean")
                ], "boolean");
            arrays.Method("every", async (array, args, token) =>
                {
                    var length = array.Count;
                    for (var i = 0; i < length && i < array.Count; i++)
                        if (!ScriptEngine.Truth(
                                await bindings.InvokeCallableAsync(args[0], [array[i], (double)i, array], token)))
                            return false;
                    return true;
                },
                [
                    new("callback", "function", CallbackParameters: ["$element", "number", "$receiver"],
                        CallbackReturnType: "boolean")
                ], "boolean");
            arrays.Method("find", async (array, args, token) =>
                {
                    var length = array.Count;
                    for (var i = 0; i < length && i < array.Count; i++)
                    {
                        var item = array[i];
                        if (ScriptEngine.Truth(
                                await bindings.InvokeCallableAsync(args[0], [item, (double)i, array], token)))
                            return item;
                    }

                    return null;
                },
                [
                    new("callback", "function", CallbackParameters: ["$element", "number", "$receiver"],
                        CallbackReturnType: "boolean")
                ],
                inferredReturnType: "$element|null");
            arrays.Method("findIndex", async (array, args, token) =>
                {
                    var length = array.Count;
                    for (var i = 0; i < length && i < array.Count; i++)
                        if (ScriptEngine.Truth(
                                await bindings.InvokeCallableAsync(args[0], [array[i], (double)i, array], token)))
                            return (double)i;
                    return -1d;
                },
                [
                    new("callback", "function", CallbackParameters: ["$element", "number", "$receiver"],
                        CallbackReturnType: "boolean")
                ], "number");
            arrays.Method("reduce", async (array, args, token) =>
            {
                if (args.Count > 2 || args.Count == 1 && array.Count == 0) throw TypeError();
                var initial = args.Count == 2;
                var result = initial ? args[1] : array[0];
                var length = array.Count;
                for (var i = initial ? 0 : 1; i < length && i < array.Count; i++)
                    result = await bindings.InvokeCallableAsync(args[0], [result, array[i], (double)i, array], token);
                return result;
            }, [
                new("callback", "function", CallbackParameters: ["$1??$element", "$element", "number", "$receiver"]),
                new("initial", Variadic: true)
            ], inferredReturnType: "$1??$element|$callback0");
        }
    }
}

namespace RitsukageGif.Scripting
{
    public static partial class StandardScriptLibrary
    {
        private static void RegisterArrays(ScriptBindingRegistry bindings)
        {
            var arrays = bindings.RegisterType<List<object?>>("array");
            arrays.Property("length", "number", array => array.Count);
            arrays.Method("push", (array, args, _) =>
            {
                array.AddRange(bindings.ValidateArrayWrites(array, args));
                return Result(array.Count);
            }, [new("values", Variadic: true, ReceiverElement: true)], "number", invalidatesReceiverElement: true);
            arrays.Method("pop", (array, _, _) => Remove(array, array.Count - 1), inferredReturnType: "$element|null");
            arrays.Method("shift", (array, _, _) => Remove(array, 0), inferredReturnType: "$element|null");
            arrays.Method("unshift", (array, args, _) =>
            {
                array.InsertRange(0, bindings.ValidateArrayWrites(array, args));
                return Result(array.Count);
            }, [new("values", Variadic: true, ReceiverElement: true)], "number", invalidatesReceiverElement: true);
            arrays.Method("join", (array, args, _) => Result(string.Join((string)args[0]!, array.Select(Text))),
                [new("separator", "string", true, ",")], "string");
            arrays.Method("concat", (array, args, _) =>
            {
                var result = new List<object?>(array);
                foreach (var item in args)
                    if (item is List<object?> other) result.AddRange(other);
                    else result.Add(item);
                return Result(result);
            }, [new("values", Variadic: true)], "array");
            arrays.Method("slice", (array, args, _) =>
                {
                    var start = Offset(args[0], array.Count);
                    var end = args[1] is null ? array.Count : Offset(args[1], array.Count);
                    return Result(array.GetRange(start, Math.Max(0, end - start)));
                }, [new("start", "number", true, 0d), new("end", "number", true)], "array",
                inferredReturnType: "$receiver");
            arrays.Method("indexOf", (array, args, _) => Result(array.IndexOf(args[0], Offset(args[1], array.Count))),
                [new("value"), new("start", "number", true, 0d)], "number");
            arrays.Method("includes",
                (array, args, _) => Result(array.IndexOf(args[0], Offset(args[1], array.Count)) >= 0),
                [new("value"), new("start", "number", true, 0d)], "boolean");
            arrays.Method("reverse", (array, _, _) =>
            {
                array.Reverse();
                return Result(array);
            }, returnType: "array", inferredReturnType: "$receiver");
            RegisterArrayCallbacks(bindings, arrays);
            arrays.Indexer((array, key) =>
            {
                var index = Index(key);
                return index < array.Count
                    ? array[index]
                    : throw new IndexOutOfRangeException(UiText.Get("ScriptIndex"));
            }, (array, key, value) =>
            {
                var index = Index(key);
                if (index > array.Count) throw new IndexOutOfRangeException(UiText.Get("ScriptIndex"));
                value = bindings.ValidateArrayWrite(array, value);
                if (index == array.Count) array.Add(value);
                else array[index] = value;
            });
        }

        private static ValueTask<object?> Remove(List<object?> array, int index)
        {
            if (array.Count == 0) return Result(null);
            var item = array[index];
            array.RemoveAt(index);
            return Result(item);
        }
    }
}

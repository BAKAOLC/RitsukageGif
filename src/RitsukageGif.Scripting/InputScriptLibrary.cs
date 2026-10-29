namespace RitsukageGif.Scripting
{
    public static class InputScriptLibrary
    {
        public static void Register(ScriptBindingRegistry bindings, IInputChannelReader? reader = null)
        {
            var cache = new Dictionary<string, (long Version, string? Json, object? Value)>(StringComparer.Ordinal);
            bindings.RegisterObjectSchema("InputData",
            [
                new("value"), new("version", "number"), new("timestamp", "number"),
                new("connected", "boolean"),
            ]);
            var inputs = bindings.RegisterTable("inputs");
            inputs.Function("names", ScriptEngine.Native(_ => reader?.Names.Cast<object?>().ToList() ?? []),
                [], "string[]");
            inputs.Function("read", ScriptEngine.Native(args => Read((string)args[0]!, reader?.Read((string)args[0]!))),
                [new("name", "string")], "InputData?");
            inputs.Function("wait", async (args, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    var name = (string)args[0]!;
                    var version = ScriptEngine.Number(args[1]);
                    var timeout = ScriptEngine.Number(args[2]);
                    if (!double.IsFinite(version) || version < -1 || version > 9007199254740991d ||
                        !version.Equals(Math.Truncate(version)) || !double.IsFinite(timeout) ||
                        timeout is < 0 or > 86400)
                        throw new ArgumentException(UiText.Get("ScriptTypeError"));
                    if (reader is null) return null;
                    var snapshot = await reader.WaitAsync(name, (long)version, timeout, token);
                    return snapshot is null ? null : Read(name, snapshot);
                },
                [new("name", "string"), new("afterVersion", "number", true, -1d), new("timeout", "number", true, 30d)],
                "InputData?");
            return;

            object? Read(string name, InputChannelData? snapshot)
            {
                if (snapshot is null)
                {
                    cache.Remove(name);
                    return null;
                }

                if (!cache.TryGetValue(name, out var previous) || previous.Version != snapshot.Version ||
                    previous.Json != snapshot.Json)
                {
                    var value = previous.Json == snapshot.Json ? previous.Value :
                        snapshot.Json is { } json ? DataScriptLibrary.ParseJson(json) : null;
                    if (cache.Count >= 256 && !cache.ContainsKey(name)) cache.Clear();
                    cache[name] = previous = (snapshot.Version, snapshot.Json, value);
                }

                return new Dictionary<string, object?>
                {
                    // Scripts may mutate their copy without changing the cached external snapshot.
                    ["value"] = Copy(previous.Value), ["version"] = (double)snapshot.Version,
                    ["timestamp"] = snapshot.Timestamp, ["connected"] = snapshot.Connected,
                };
            }
        }

        private static object? Copy(object? value)
        {
            return value switch
            {
                Dictionary<string, object?> fields => fields.ToDictionary(pair => pair.Key, pair => Copy(pair.Value),
                    StringComparer.Ordinal),
                List<object?> items => items.Select(Copy).ToList(),
                _ => value,
            };
        }
    }
}

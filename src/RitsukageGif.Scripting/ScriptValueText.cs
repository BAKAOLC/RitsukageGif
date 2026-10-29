using System.Globalization;

namespace RitsukageGif.Scripting
{
    public static class ScriptValueText
    {
        public static string Convert(object? value)
        {
            return value is List<object?> or Dictionary<string, object?> or ScriptMap or ScriptSet or ScriptResult
                or ScriptOption
                ? Convert(value, new(ReferenceEqualityComparer.Instance))
                : ConvertPrimitive(value);
        }

        private static string Convert(object? value, HashSet<object> path)
        {
            if (value is List<object?> or Dictionary<string, object?> or ScriptMap or ScriptSet or ScriptResult
                or ScriptOption)
            {
                if (!path.Add(value) || path.Count > 64)
                    throw new InvalidOperationException(UiText.Get("ScriptTypeError"));
                try
                {
                    return value switch
                    {
                        List<object?> array => "[" + string.Join(", ", array.Select(item => Convert(item, path))) + "]",
                        Dictionary<string, object?> map => "{" + string.Join(", ",
                            map.Select(pair => pair.Key + ": " + Convert(pair.Value, path))) + "}",
                        ScriptMap map => "Map(" + Convert(map.Entries(), path) + ")",
                        ScriptSet set => "Set(" + Convert(set.Values.ToList(), path) + ")",
                        ScriptResult result => (result.IsOk ? "Ok(" : "Err(") + Convert(result.Value, path) + ")",
                        ScriptOption option => option.HasValue ? "Some(" + Convert(option.Value, path) + ")" : "None",
                        _ => throw new InvalidOperationException(UiText.Get("ScriptTypeError")),
                    };
                }
                finally
                {
                    path.Remove(value);
                }
            }

            return ConvertPrimitive(value);
        }

        private static string ConvertPrimitive(object? value)
        {
            return value switch
            {
                null => "null", string text => text, bool boolean => boolean ? "true" : "false",
                double number => number.ToString("R", CultureInfo.InvariantCulture),
                IScriptCallable or ScriptNative => "fn", ScriptTableBinding table => table.Name,
                Enum enumeration => enumeration.ToString(), _ => "object",
            };
        }
    }
}

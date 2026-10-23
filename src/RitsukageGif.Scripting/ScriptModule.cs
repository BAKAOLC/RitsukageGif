namespace RitsukageGif.Scripting
{
    public sealed class ScriptModule
    {
        private readonly Dictionary<string, Func<object?>> _exports = new(StringComparer.Ordinal);
        internal object? LegacyValue { get; set; }
        internal bool HasExports { get; set; }
        public IEnumerable<string> Names => _exports.Keys;

        internal Dictionary<string, object?> Snapshot() => HasExports
            ? _exports.ToDictionary(pair => pair.Key, pair => pair.Value(), StringComparer.Ordinal)
            : LegacyValue is Dictionary<string, object?> values
                ? new(values, StringComparer.Ordinal)
                : new(StringComparer.Ordinal) { ["default"] = LegacyValue };

        internal void Export(string name, Func<object?> getter)
        {
            if (!_exports.TryAdd(name, getter))
                throw new InvalidOperationException(UiText.Format("DslDuplicate", name));
        }

        public object? Get(string name)
        {
            if (_exports.TryGetValue(name, out var getter)) return getter();
            if (!HasExports)
            {
                if (name == "default") return LegacyValue;
                if (LegacyValue is Dictionary<string, object?> values && values.TryGetValue(name, out var value))
                    return value;
            }

            throw new InvalidOperationException(UiText.Format("DslUnknownName", name));
        }
    }
}

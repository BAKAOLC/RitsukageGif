namespace RitsukageGif.Scripting
{
    public sealed class ScriptMap
    {
        private static readonly object NullKey = new();
        private readonly OrderedDictionary<object, object?> _values = new();
        public int Count => _values.Count;
        public IEnumerable<object?> Keys => _values.Keys.Select(key => ReferenceEquals(key, NullKey) ? null : key);
        public IEnumerable<object?> Values => _values.Values;
        public object? Get(object? key) => _values.TryGetValue(key ?? NullKey, out var value) ? value : null;
        public bool Has(object? key) => _values.ContainsKey(key ?? NullKey);
        public void Set(object? key, object? value) => _values[key ?? NullKey] = value;
        public bool Delete(object? key) => _values.Remove(key ?? NullKey);
        public void Clear() => _values.Clear();

        public List<object?> Entries() => _values.Select(pair => (object?)new List<object?>
            { ReferenceEquals(pair.Key, NullKey) ? null : pair.Key, pair.Value }).ToList();
    }
}

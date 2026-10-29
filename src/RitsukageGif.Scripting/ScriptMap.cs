namespace RitsukageGif.Scripting
{
    public sealed class ScriptMap
    {
        private static readonly object NullKey = new();
        private readonly OrderedDictionary<object, object?> _values = new();
        public int Count => _values.Count;
        public IEnumerable<object?> Keys => _values.Keys.Select(key => ReferenceEquals(key, NullKey) ? null : key);
        public IEnumerable<object?> Values => _values.Values;

        public object? Get(object? key)
        {
            return _values.GetValueOrDefault(key ?? NullKey);
        }

        public bool Has(object? key)
        {
            return _values.ContainsKey(key ?? NullKey);
        }

        public void Set(object? key, object? value)
        {
            _values[key ?? NullKey] = value;
        }

        public bool Delete(object? key)
        {
            return _values.Remove(key ?? NullKey);
        }

        public void Clear()
        {
            _values.Clear();
        }

        public List<object?> Entries()
        {
            return
            [
                .. _values.Select(object? (pair) => new List<object?>
                    { ReferenceEquals(pair.Key, NullKey) ? null : pair.Key, pair.Value }),
            ];
        }
    }
}

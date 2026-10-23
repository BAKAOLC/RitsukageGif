namespace RitsukageGif.Scripting
{
    public sealed class ScriptSet
    {
        private readonly ScriptMap _values = new();
        public int Count => _values.Count;
        public IEnumerable<object?> Values => _values.Keys;
        public void Add(object? value) => _values.Set(value, null);
        public bool Has(object? value) => _values.Has(value);
        public bool Delete(object? value) => _values.Delete(value);
        public void Clear() => _values.Clear();
    }
}

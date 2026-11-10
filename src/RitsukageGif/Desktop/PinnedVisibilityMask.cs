namespace RitsukageGif.Desktop
{
    internal sealed class PinnedVisibilityMask<T> where T : class
    {
        private readonly List<T> _order = [];
        private readonly HashSet<T> _hidden = new(ReferenceEqualityComparer.Instance);
        internal bool Active { get; private set; }
        internal IReadOnlyList<T> Order => _order;

        internal void Initialize(bool active)
        {
            Active = active;
        }

        internal void Begin(IEnumerable<T> visible, IReadOnlySet<T> protectedItems)
        {
            Active = true;
            _order.Clear();
            _hidden.Clear();
            foreach (var item in visible)
            {
                _order.Add(item);
                if (!protectedItems.Contains(item)) _hidden.Add(item);
            }
        }

        internal bool IsHidden(T item)
        {
            return _hidden.Contains(item);
        }

        internal void Attach(T item, bool show)
        {
            if (!Active) return;
            if (!_order.Contains(item)) _order.Add(item);
            if (!show) _hidden.Add(item);
        }

        internal void Remove(T item)
        {
            _order.Remove(item);
            _hidden.Remove(item);
        }

        internal void Clear()
        {
            Active = false;
            _order.Clear();
            _hidden.Clear();
        }
    }
}

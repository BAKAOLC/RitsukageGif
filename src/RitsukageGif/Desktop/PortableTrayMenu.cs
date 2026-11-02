using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed class PortableTrayMenu(Action changed) : IDisposable
    {
        private readonly Dictionary<NativeMenuItemBase, int> _identities = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<int, NativeMenuItem> _items = [];
        private readonly Dictionary<int, NativeMenu> _menus = [];
        private readonly List<NativeMenuItemBase> _watchedItems = [];
        private readonly List<NativeMenu> _watchedMenus = [];
        private NativeMenu? _root;
        private int _nextId;
        private bool _queued, _disposed;

        public void Dispose()
        {
            _disposed = true;
            Unwatch();
            _identities.Clear();
            _items.Clear();
            _menus.Clear();
            _root = null;
        }

        internal void SetMenu(NativeMenu? menu)
        {
            Unwatch();
            _root = menu;
            Refresh();
        }

        internal IReadOnlyList<PortableTrayMenuEntry> Capture()
        {
            Unwatch();
            _items.Clear();
            _menus.Clear();
            var live = new HashSet<NativeMenuItemBase>(ReferenceEqualityComparer.Instance);
            var entries = new List<PortableTrayMenuEntry>();
            if (_root is not null) Add(_root, 0);
            foreach (var old in _identities.Keys.Where(item => !live.Contains(item)).ToArray()) _identities.Remove(old);
            return entries;

            void Add(NativeMenu menu, int parent)
            {
                _menus[parent] = menu;
                _watchedMenus.Add(menu);
                ((INotifyCollectionChanged)menu.Items).CollectionChanged += CollectionChanged;
                foreach (var item in menu.Items)
                {
                    live.Add(item);
                    _watchedItems.Add(item);
                    item.PropertyChanged += PropertyChanged;
                    if (!_identities.TryGetValue(item, out var id)) _identities.Add(item, id = ++_nextId);
                    if (item is NativeMenuItemSeparator)
                    {
                        entries.Add(new(id, parent, true, 0, false, true, "", null));
                        continue;
                    }

                    if (item is not NativeMenuItem { IsVisible: true } action) continue;
                    _items[id] = action;
                    entries.Add(new(id, parent, action.IsEnabled, (int)action.ToggleType, action.IsChecked, false,
                        action.Header ?? "", action.Gesture));
                    if (action.Menu is { } submenu) Add(submenu, id);
                }
            }
        }

        internal void RequestUpdate(int id)
        {
            if (_disposed) return;
            if (_menus.TryGetValue(id, out var menu))
                ((INativeMenuExporterEventsImplBridge)menu).RaiseNeedsUpdate();
            changed();
        }

        internal void Invoke(int id)
        {
            if (!_disposed && _items.TryGetValue(id, out var item) && item is { IsVisible: true, IsEnabled: true })
                ((INativeMenuItemExporterEventsImplBridge)item).RaiseClicked();
        }

        internal void Open(int id)
        {
            if (_menus.TryGetValue(id, out var menu)) ((INativeMenuExporterEventsImplBridge)menu).RaiseOpening();
        }

        internal void Close(int id)
        {
            if (_menus.TryGetValue(id, out var menu)) ((INativeMenuExporterEventsImplBridge)menu).RaiseClosed();
        }

        private void PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            Refresh();
        }

        private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
        {
            Refresh();
        }

        private void Refresh()
        {
            if (_disposed || _queued) return;
            _queued = true;
            Dispatcher.UIThread.Post(() =>
            {
                _queued = false;
                if (!_disposed) changed();
            });
        }

        private void Unwatch()
        {
            foreach (var item in _watchedItems) item.PropertyChanged -= PropertyChanged;
            foreach (var menu in _watchedMenus)
                ((INotifyCollectionChanged)menu.Items).CollectionChanged -= CollectionChanged;
            _watchedItems.Clear();
            _watchedMenus.Clear();
        }
    }
}

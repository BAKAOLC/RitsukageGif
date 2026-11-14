using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class AnnotationToolsStrip
    {
        private readonly HashSet<Key> _menuKeys = [];
        private Flyout? _openMenu;
        private Button[] _menuItems = [];
        private int _currentMenuItem = -1;
        private readonly List<Window> _menuOwners = [];

        private void AttachMenuOwners()
        {
            DetachMenuOwners();
            for (var owner = TopLevel.GetTopLevel(this) as Window; owner is not null; owner = owner.Owner as Window)
            {
                _menuOwners.Add(owner);
                owner.Deactivated += MenuOwnerDeactivated;
                owner.Opened += MenuOwnerOpened;
            }
        }

        private void DetachMenuOwners()
        {
            foreach (var owner in _menuOwners)
            {
                owner.Deactivated -= MenuOwnerDeactivated;
                owner.Opened -= MenuOwnerOpened;
            }

            _menuOwners.Clear();
            _menuKeys.Clear();
        }

        private void MenuOwnerDeactivated(object? sender, EventArgs args)
        {
            _menuKeys.Clear();
        }

        private void MenuOwnerOpened(object? sender, EventArgs args)
        {
            AttachMenuOwners();
        }

        private void ConfigureMenu(Flyout menu, Panel content)
        {
            var items = content.Children.OfType<Button>().ToArray();
            TopLevel? popup = null;
            var index = 0;
            foreach (var item in items)
            {
                var selectedIndex = index++;
                item.PointerEntered += (_, _) =>
                {
                    if (ReferenceEquals(_openMenu, menu) && item.IsEffectivelyEnabled)
                        SelectMenuItem(selectedIndex);
                };
            }

            menu.Opened += (_, _) =>
            {
                _openMenu = menu;
                _menuItems = items;
                _currentMenuItem = -1;
                popup = TopLevel.GetTopLevel(content);
                if (popup is null) return;
                popup.AddHandler(KeyDownEvent, MenuKeyDown, RoutingStrategies.Tunnel);
                popup.AddHandler(KeyUpEvent, MenuKeyUp, RoutingStrategies.Tunnel);
                popup.Focusable = true;
                popup.Focus();
            };
            menu.Closed += (_, _) =>
            {
                if (popup is not null)
                {
                    popup.RemoveHandler(KeyDownEvent, MenuKeyDown);
                    popup.RemoveHandler(KeyUpEvent, MenuKeyUp);
                    popup = null;
                }

                if (!ReferenceEquals(_openMenu, menu)) return;
                SelectMenuItem(-1);
                _openMenu = null;
                _menuItems = [];
            };
        }

        internal bool HandleMenuKey(KeyEventArgs args, bool down)
        {
            if (args.Handled) return true;
            if (!down)
            {
                if (!_menuKeys.Remove(args.Key)) return false;
                args.Handled = true;
                return true;
            }

            if (_openMenu is null && _menuKeys.Contains(args.Key))
            {
                args.Handled = true;
                return true;
            }

            if (_openMenu is null || args.Key is not (Key.Up or Key.Down or Key.Left or Key.Right or Key.Enter
                    or Key.Escape or Key.Tab or Key.Home or Key.End)) return false;
            var firstPress = _menuKeys.Add(args.Key);
            args.Handled = true;
            // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
            switch (args.Key)
            {
                case Key.Escape:
                    if (firstPress) _openMenu.Hide();
                    break;
                case Key.Enter:
                    if (!firstPress) break;
                    if (_currentMenuItem < 0) MoveMenuItem(1);
                    else if (_menuItems[_currentMenuItem] is { IsEffectivelyEnabled: true } item)
                        item.RaiseEvent(new(Button.ClickEvent));
                    break;
                case Key.Up:
                    MoveMenuItem(-1);
                    break;
                case Key.Down:
                    MoveMenuItem(1);
                    break;
                case Key.Tab:
                    if (!OperatingSystem.IsMacOS())
                        MoveMenuItem(args.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1);
                    break;
            }

            return true;
        }

        private void MenuKeyDown(object? sender, KeyEventArgs args)
        {
            HandleMenuKey(args, true);
        }

        private void MenuKeyUp(object? sender, KeyEventArgs args)
        {
            HandleMenuKey(args, false);
        }

        private void MoveMenuItem(int direction)
        {
            var index = _currentMenuItem < 0 ? direction > 0 ? -1 : 0 : _currentMenuItem;
            // ReSharper disable once ForCanBeConvertedToForeach
            for (var count = 0; count < _menuItems.Length; count++)
            {
                index = (index + direction + _menuItems.Length) % _menuItems.Length;
                if (!_menuItems[index].IsEffectivelyVisible || !_menuItems[index].IsEffectivelyEnabled) continue;
                SelectMenuItem(index);
                return;
            }
        }

        private void SelectMenuItem(int index)
        {
            if (_currentMenuItem == index) return;
            if (_currentMenuItem >= 0) _menuItems[_currentMenuItem].Background = Brushes.Transparent;
            _currentMenuItem = index;
            if (index >= 0) _menuItems[index].Background = AppTheme.Surface;
        }
    }
}

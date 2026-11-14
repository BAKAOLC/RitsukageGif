using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal static class TrayContextPopup
    {
        private static Window? _anchor;
        private static PortableTrayMenu? _model;

        internal static void Show(PortableTrayMenu model, int x, int y)
        {
            Close();
            model.RequestUpdate(0);
            var entries = model.Capture();
            var target = new Border { Width = 1, Height = 1 };
            var anchor = new Window
            {
                ShowInTaskbar = false, ShowActivated = false, CanResize = false,
                WindowDecorations = WindowDecorations.None, Width = 1, Height = 1, Opacity = 0,
                WindowStartupLocation = WindowStartupLocation.Manual, Position = new(x, y),
                Background = Brushes.Transparent, Content = target,
            };
            var menu = new ContextMenu { Placement = PlacementMode.Bottom, PlacementTarget = target };
            var byParent = entries.ToLookup(entry => entry.Parent);
            var icons = new List<Bitmap>();
            foreach (var entry in byParent[0]) menu.Items.Add(Create(entry));
            target.ContextMenu = menu;
            menu.Opened += (_, _) => model.Open(0);
            menu.Closed += (_, _) =>
            {
                model.Close(0);
                if (!ReferenceEquals(_anchor, anchor)) return;
                _anchor = null;
                _model = null;
                Dispatcher.UIThread.Post(anchor.Close);
            };
            anchor.Closed += (_, _) =>
            {
                foreach (var icon in icons) icon.Dispose();
                if (!ReferenceEquals(_anchor, anchor)) return;
                _anchor = null;
                _model = null;
            };
            _anchor = anchor;
            _model = model;
            anchor.Show();
            menu.Open(target);
            return;

            Control Create(PortableTrayMenuEntry entry)
            {
                if (entry.Separator) return new Separator();
                var item = new MenuItem
                {
                    Header = entry.Label, IsEnabled = entry.Enabled, IsChecked = entry.Checked,
                    ToggleType = (MenuItemToggleType)entry.Toggle, InputGesture = entry.Gesture,
                };
                if (entry.IconPng is { } png)
                {
                    using var stream = new MemoryStream(png, false);
                    var icon = new Bitmap(stream);
                    icons.Add(icon);
                    item.Icon = new Image { Source = icon, Width = 16, Height = 16 };
                }

                foreach (var child in byParent[entry.Id]) item.Items.Add(Create(child));
                item.Click += (_, args) =>
                {
                    if (ReferenceEquals(args.Source, item)) model.Invoke(entry.Id);
                };
                return item;
            }
        }

        internal static void Close()
        {
            var anchor = _anchor;
            _anchor = null;
            _model = null;
            if (anchor is null) return;
            if (anchor.Content is Border { ContextMenu: { } menu }) menu.Close();
            anchor.Close();
        }

        internal static void CloseFor(PortableTrayMenu model)
        {
            if (ReferenceEquals(_model, model)) Close();
        }
    }
}

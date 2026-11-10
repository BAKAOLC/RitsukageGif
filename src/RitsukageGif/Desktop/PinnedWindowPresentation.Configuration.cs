using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedWindowPresentation
    {
        internal Func<bool> ConfigurationVisibleProvider { get; set; } = () => false;
        internal Func<Task>? ConfigurationRequested { get; set; }

        internal void AddConfigurationCommand(ContextMenu menu)
        {
            var item = new MenuItem { Tag = "configuration", IsVisible = false };
            item.Bind(HeaderedSelectingItemsControl.HeaderProperty, LiveText.Resource("PinnedConfiguration"));
            item.Click += async (_, _) => await RunMenuAsync(() =>
                ConfigurationRequested?.Invoke() ?? Task.CompletedTask);
            menu.Items.Add(item);
            menu.Opened += (_, _) => item.IsVisible = ConfigurationVisibleProvider();
        }
    }
}

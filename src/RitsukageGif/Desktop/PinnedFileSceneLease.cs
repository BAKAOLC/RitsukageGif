using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    internal sealed class PinnedFileSceneLease(Grid owner, Canvas host, ItemsControl list) : IDisposable
    {
        internal ItemsControl List { get; } = list;

        public void Dispose()
        {
            owner.Children.Remove(host);
        }
    }
}

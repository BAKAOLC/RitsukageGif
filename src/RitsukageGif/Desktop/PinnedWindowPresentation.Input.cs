using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedWindowPresentation
    {
        private PinnedInputOrigin _menuOrigin;
        private PinnedInputOrigin? _requestedMenuOrigin;

        internal void ConnectMenu(ContextMenu menu)
        {
            _owner.AddHandler(InputElement.ContextRequestedEvent, (sender, args) =>
            {
                _requestedMenuOrigin = args.TryGetPosition(_owner, out _)
                    ? PinnedInputContext.Current & PinnedInputOrigin.Keyboard
                    : PinnedInputContext.Current | PinnedInputOrigin.Keyboard;
            }, RoutingStrategies.Tunnel);
            menu.Opened += (_, _) =>
            {
                _menuOrigin = (_requestedMenuOrigin ?? PinnedInputContext.Current) & PinnedInputOrigin.Keyboard;
                _requestedMenuOrigin = null;
            };
            menu.Closed += (_, _) => _requestedMenuOrigin = null;
        }

        internal void OpenMenu(ContextMenu menu, Control placement)
        {
            _requestedMenuOrigin = PinnedInputContext.Current & PinnedInputOrigin.Keyboard;
            menu.Open(placement);
        }

        internal async Task RunMenuAsync(Func<Task> operation)
        {
            using var input = PinnedInputContext.Use(_menuOrigin);
            await operation();
        }
    }
}

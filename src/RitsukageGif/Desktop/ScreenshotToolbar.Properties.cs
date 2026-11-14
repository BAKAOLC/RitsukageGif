using Avalonia.Controls;
using Avalonia.Input;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotToolbar
    {
        private readonly Dictionary<SelectionCommand, Button> _extraCommands = [];

        internal bool HandleAnnotationMenuKey(KeyEventArgs args, bool down)
        {
            return _annotationTools.HandleMenuKey(args, down);
        }

        private void ArrowCommentRequested()
        {
            if (_desktopPlacement is not null && !_desktopPrimary) return;
            _propertyPresenter.EditComment();
            _contextFrame.IsVisible = _propertyPresenter.HasContext;
        }
    }
}

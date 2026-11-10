using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotToolbar
    {
        private readonly Dictionary<SelectionCommand, Button> _extraCommands = [];

        private void ArrowCommentRequested()
        {
            _propertyPresenter.EditComment();
            _contextFrame.IsVisible = _propertyPresenter.HasContext;
        }
    }
}

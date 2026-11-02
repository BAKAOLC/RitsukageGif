using Avalonia.Controls;
using Avalonia.Layout;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotToolbar
    {
        private WrapPanel? _documentStrip;

        internal Button AddDocumentAction(string key, Action action, string? iconKey = null)
        {
            if (_documentStrip is null)
            {
                _documentStrip = new() { Orientation = Orientation.Horizontal };
                _commandRow.Children.Insert(_commandRow.Children.IndexOf(_more), Group(_documentStrip));
            }

            var button = SmallButton(key, action, ToolbarIcon.TryGeometry(iconKey ?? key));
            _documentStrip.Children.Add(button);
            return button;
        }
    }
}

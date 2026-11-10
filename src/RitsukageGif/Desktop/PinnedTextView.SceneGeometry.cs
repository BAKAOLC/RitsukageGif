using Avalonia;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedTextView
    {
        internal Size SceneSize => new(TextArea.TextView.Bounds.Width, TextArea.TextView.DocumentHeight);
    }
}

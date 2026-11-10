using Avalonia;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedRichView
    {
        internal Visual SceneVisual => _surface;

        internal Size SceneSize => _layout!.Size;

        internal IReadOnlyDictionary<string, Size> SceneImageSizes =>
            new Dictionary<string, Size>(_naturalSizes, StringComparer.Ordinal);

        internal event EventHandler? SceneContentChanged;
    }
}

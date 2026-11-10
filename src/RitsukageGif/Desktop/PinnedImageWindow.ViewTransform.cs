using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        private readonly Grid _sceneLayer;
        private readonly LayoutTransformControl _sceneTransform;
        private readonly Grid _viewLayer;
        private PinnedViewTransform _viewTransform;

        private void ApplyViewTransform(PinnedImageTransform operation)
        {
            CancelThumbnailLayout();
            ClearTextSelection();
            _viewTransform = _viewTransform.Apply(operation);
            _viewport.Offset = default;
            UpdateSize();
            RefreshHdrPreview();
            RestoreToScreen();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

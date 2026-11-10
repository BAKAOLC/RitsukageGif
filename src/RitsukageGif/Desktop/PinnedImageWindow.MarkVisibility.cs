namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        private bool _marksHidden;
        private bool IsMarkEditing => _annotation is not null;
        private bool IsMarkHidden => _annotationScene is { } scene ? !scene.AnnotationsVisible : _marksHidden;

        private void SetMarkHidden(bool hidden)
        {
            if (_closed || IsMarkEditing || IsMarkHidden == hidden) return;
            _marksHidden = hidden;
            if (_annotationScene is { } scene && scene.AnnotationsVisible == hidden) scene.ToggleAnnotations();
            _textSelectionSurface?.InvalidateVisual();
            InvalidateInputUi();
            RefreshHdrPreview();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

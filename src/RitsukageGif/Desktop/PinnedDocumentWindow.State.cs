using Avalonia;
using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedDocumentWindow
    {
        public PinnedDocumentState CaptureDocumentState()
        {
            if (_closedDocumentState is { } closed) return closed;
            var scroll = ContentScrollOffset();
            var saved = new PinnedDocumentState(new()
            {
                Id = Id, Group = Group, X = Position.X, Y = Position.Y, Zoom = _zoom,
                Opacity = Opacity, Locked = _locked, Topmost = Topmost,
                Visible = IsVisible && WindowState != WindowState.Minimized,
                CreatedAtUtcTicks = _createdAtUtcTicks,
                ScrollX = scroll.X, ScrollY = scroll.Y,
            }, Document)
            {
                IgnoreFormat = IgnoreFormat, Selectable = Selectable, Width = Width, Height = Height,
                Thumbnail = CaptureDocumentThumbnail(), ThumbnailRequest = _thumbnailRequest,
                Annotations = _annotationScene?.CaptureDocument(), AnnotationScaling = _annotationScaling,
                FileListMaximumSize = _fileListMaximumSize,
                Translation = CaptureInlineTranslationState(),
            };
            Presentation.Capture(saved.Window);
            return saved;
        }

        private Vector ContentScrollOffset()
        {
            return _textView?.ScrollOffset ?? _viewport.Offset;
        }

        private void SetContentScrollOffset(Vector offset)
        {
            _viewport.Offset = offset;
            if (_textView is { } text) text.ScrollOffset = offset;
        }

        public void RestoreDocumentState(PinnedDocumentState saved)
        {
            _restoredLayout = true;
            var state = saved.Window;
            Id = state.Id;
            Group = state.Group;
            Presentation.Restore(state);
            Position = new(state.X, state.Y);
            Width = saved.Width;
            Height = saved.Height;
            SetZoom(state.Zoom);
            Opacity = state.Opacity;
            _locked = state.Locked;
            Topmost = state.Topmost;
            _createdAtUtcTicks = state.CreatedAtUtcTicks;
            var rebuild = IgnoreFormat != saved.IgnoreFormat;
            if (Document is PinnedFilesDocument)
            {
                var maximumSize = saved.FileListMaximumSize ?? CreateFileListMaximumSize();
                if (_fileListMaximumSize != maximumSize)
                {
                    _fileListMaximumSize = maximumSize;
                    _fileLayoutSnapshot = null;
                    rebuild = true;
                }
            }

            IgnoreFormat = saved.IgnoreFormat;
            Selectable = saved.Selectable;
            if (RestoreInlineTranslationState(saved.Translation) || rebuild) RebuildContent();
            else _textView?.SetSelectable(Selectable);
            UpdateLayout();
            SetContentScrollOffset(new(state.ScrollX, state.ScrollY));
            RestoreDocumentThumbnail(saved.Thumbnail);
            _thumbnailRequest = saved.ThumbnailRequest;
            ApplyDocumentThumbnailRequest();
            if (saved.Annotations is { } annotations)
            {
                var session = EnsureDocumentAnnotations();
                _annotationScaling = saved.AnnotationScaling;
                session.RestoreDocument(annotations);
                DocumentAnnotationLayoutChanged(true);
            }

            RefreshPreferencePresentation();
            if (!state.Visible) HideImage();
        }
    }
}

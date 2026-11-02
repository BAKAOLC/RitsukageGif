using Avalonia;
using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedDocumentWindow
    {
        public PinnedDocumentState CaptureDocumentState()
        {
            var scroll = ContentScrollOffset();
            var saved = new PinnedDocumentState(new()
            {
                Id = Id, Group = Group, X = Position.X, Y = Position.Y, Zoom = _zoom,
                Opacity = Opacity, Locked = _locked, Topmost = Topmost,
                Visible = IsVisible && WindowState != WindowState.Minimized,
                CreatedAtUtcTicks = _createdAtUtcTicks,
                ScrollX = scroll.X, ScrollY = scroll.Y,
            }, Document) { IgnoreFormat = IgnoreFormat, Selectable = Selectable, Width = Width, Height = Height };
            Presentation.Capture(saved.Window);
            return saved;
        }

        private Vector ContentScrollOffset()
        {
            return _textView?.ScrollOffset ?? _fileList?.Scroll?.Offset ?? _viewport.Offset;
        }

        private void SetContentScrollOffset(Vector offset)
        {
            _viewport.Offset = offset;
            if (_fileList?.Scroll is { } scroll) scroll.Offset = offset;
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
            IgnoreFormat = saved.IgnoreFormat;
            Selectable = saved.Selectable;
            if (rebuild) RebuildContent();
            else _textView?.SetSelectable(Selectable);
            UpdateLayout();
            SetContentScrollOffset(new(state.ScrollX, state.ScrollY));
            RefreshPreferencePresentation();
            if (!state.Visible) HideImage();
        }
    }
}

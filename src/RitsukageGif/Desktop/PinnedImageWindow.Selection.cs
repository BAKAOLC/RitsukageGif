using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        private Border? _selectionOutline;
        private bool IsSelected { get; set; }
        public bool WasDestroyed { get; private set; }
        public Action<PinnedImageWindow>? SelectionRequested { get; set; }
        public Action<PinnedImageWindow, bool>? CloseRequested { get; set; }
        public Action<PinnedImageWindow, Avalonia.PixelPoint>? MoveSelectionRequested { get; set; }

        public void SetSelected(bool selected)
        {
            if (IsSelected == selected) return;
            IsSelected = selected;
            _selectionOutline ??= new()
                { BorderBrush = AppTheme.Accent, BorderThickness = new(2), IsHitTestVisible = false };
            if (selected) _content.Children.Add(_selectionOutline);
            else _content.Children.Remove(_selectionOutline);
        }

        public void DestroyForGroup()
        {
            WasDestroyed = true;
            Close();
        }

        public void CloseImage(bool destroy)
        {
            WasDestroyed = destroy;
            Close();
        }

        private void RequestClose(bool destroy = false)
        {
            if (CloseRequested is { } request) request(this, destroy);
            else if (!IsCloseLocked) CloseImage(destroy);
        }

        private void NudgeWindow(int dx, int dy)
        {
            if (IsInteractionLocked) return;
            var previous = Position;
            Position = new((int)Math.Clamp((long)previous.X + dx, int.MinValue, int.MaxValue),
                (int)Math.Clamp((long)previous.Y + dy, int.MinValue, int.MaxValue));
            MoveSelectionRequested?.Invoke(this, new(Position.X - previous.X, Position.Y - previous.Y));
        }
    }
}

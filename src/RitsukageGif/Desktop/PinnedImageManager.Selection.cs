using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedImageManager
    {
        private readonly HashSet<PinnedImageWindow> _selectedImages = [];

        private void ToggleSelection(PinnedImageWindow window)
        {
            Dispatcher.UIThread.VerifyAccess();
            if (!_windows.Contains(window)) return;
            if (!_selectedImages.Add(window)) _selectedImages.Remove(window);
            window.SetSelected(_selectedImages.Contains(window));
        }

        private void CloseSelection(PinnedImageWindow source, bool destroy)
        {
            var images = _selectedImages.Contains(source) && _selectedImages.Count > 1
                ? _selectedImages.ToArray()
                : [source];
            foreach (var image in images)
            {
                if (image.IsCloseLocked) continue;
                if (destroy) _closedImages.RemoveAll(entry => entry.State.Id == image.Id);
                image.CloseImage(destroy);
            }
        }

        private void MoveSelection(PinnedImageWindow source, Avalonia.PixelPoint delta)
        {
            if (!_selectedImages.Contains(source)) return;
            foreach (var window in _selectedImages)
                if (window != source && !window.IsInteractionLocked)
                    window.Position = new(
                        (int)Math.Clamp((long)window.Position.X + delta.X, int.MinValue, int.MaxValue),
                        (int)Math.Clamp((long)window.Position.Y + delta.Y, int.MinValue, int.MaxValue));
        }
    }
}

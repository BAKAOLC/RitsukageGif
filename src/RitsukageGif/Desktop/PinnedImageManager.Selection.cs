using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedImageManager
    {
        private readonly HashSet<IPinnedWindow> _selectedImages = [];

        private void ToggleSelection(IPinnedWindow window)
        {
            Dispatcher.UIThread.VerifyAccess();
            if (!AllWindows.Contains(window)) return;
            if (!_selectedImages.Add(window)) _selectedImages.Remove(window);
            window.SetSelected(_selectedImages.Contains(window));
        }

        private void CloseSelection(IPinnedWindow source, bool destroy)
        {
            var images = _selectedImages.Contains(source) && _selectedImages.Count > 1
                ? _selectedImages.ToArray()
                : [source];
            foreach (var image in images)
            {
                if (images.Length == 1 && image.IsCloseLocked) continue;
                if (destroy)
                {
                    _closedImages.RemoveAll(entry => entry.State.Id == image.Id);
                    _closedDocuments.RemoveAll(entry => entry.State.Window.Id == image.Id);
                }

                image.CloseImage(destroy);
            }
        }

        private void MoveSelection(IPinnedWindow source, Avalonia.PixelPoint delta)
        {
            if (!_selectedImages.Contains(source)) return;
            foreach (var window in _selectedImages)
                if (window != source && !window.IsInteractionLocked)
                    window.Window.Position = new(
                        (int)Math.Clamp((long)window.Window.Position.X + delta.X, int.MinValue, int.MaxValue),
                        (int)Math.Clamp((long)window.Window.Position.Y + delta.Y, int.MinValue, int.MaxValue));
        }
    }
}

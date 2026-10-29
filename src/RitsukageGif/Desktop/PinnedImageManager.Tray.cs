using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedImageManager
    {
        public bool HasRestorableSession => _unrestoredStates.Count != 0;
        public bool HasVisibleImages => _windows.Any(window => window.IsVisible);
        public IReadOnlyList<PinnedImageWindow> Images => _windows;

        public void ShowImage(PinnedImageWindow window)
        {
            Dispatcher.UIThread.VerifyAccess();
            if (_windows.Contains(window)) RevealWindow(window);
        }

        public void CloseAll()
        {
            Dispatcher.UIThread.VerifyAccess();
            foreach (var window in _windows.ToArray())
                if (!window.IsCloseLocked)
                    window.CloseImage(false);
        }
    }
}

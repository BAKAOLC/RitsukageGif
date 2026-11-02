using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedImageManager
    {
        public bool HasRestorableSession => _unrestoredStates.Count != 0 || _unrestoredDocuments.Count != 0;
        public bool HasVisibleImages => AllWindows.Any(window => window.IsVisible);

        public IReadOnlyList<IPinnedWindow> Images =>
            [.. AllWindows.Where(window => window.Group == Preferences.ActiveGroup)];

        public void ShowImage(IPinnedWindow window)
        {
            Dispatcher.UIThread.VerifyAccess();
            if (!AllWindows.Contains(window)) return;
            SetActiveGroup(window.Group);
            RevealWindow(window);
        }

        public void CloseAll()
        {
            Dispatcher.UIThread.VerifyAccess();
            foreach (var window in AllWindows.Where(window => window.Group == Preferences.ActiveGroup).ToArray())
                if (AllWindows.Contains(window))
                    CloseSelection(window, false);
        }
    }
}

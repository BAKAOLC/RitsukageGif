using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedImageManager
    {
        public bool HasRestorableSession => _unrestoredStates.Count != 0 || _unrestoredDocuments.Count != 0;

        public void CloseAll()
        {
            Dispatcher.UIThread.VerifyAccess();
            foreach (var window in AllWindows.Where(window => window.Group == Preferences.ActiveGroup).ToArray())
                if (AllWindows.Contains(window))
                    CloseSelection(window, false);
        }
    }
}

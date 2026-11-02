using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SelectionCoordinator
    {
        private int _selectionDialogDepth;

        internal IDisposable BeginSelectionDialog()
        {
            Dispatcher.UIThread.VerifyAccess();
            _selectionDialogDepth++;
            RefreshWindowsKeyGuard();
            return new SelectionDialogScope(() =>
            {
                Dispatcher.UIThread.VerifyAccess();
                _selectionDialogDepth--;
                RefreshWindowsKeyGuard();
                FocusChanged();
            });
        }
    }
}

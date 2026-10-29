using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SelectionCoordinator
    {
        private bool _autoConfirmPending;
        private bool _initialScrollingStarted;

        internal bool AutoConfirm { get; init; }
        internal bool StartWithScrolling { get; init; }

        private void QueueAutoConfirm()
        {
            if (!AutoConfirm || !ScreenshotMode || _state.Selected.IsEmpty || _initialScrollingStarted) return;
            _autoConfirmPending = true;
            ResumeAutoConfirm();
        }

        private void ResumeAutoConfirm()
        {
            if (_autoConfirmPending) Dispatcher.UIThread.Post(TryAutoConfirm);
        }

        private void TryAutoConfirm()
        {
            if (!_autoConfirmPending || _finished || _screenshotCompleting || NeedsWindowScreenshot ||
                _state.Dragging || _state.Selected.IsEmpty || !AllInputReleased ||
                Annotations is not { IsBusy: false, IsDrawing: false }) return;
            _autoConfirmPending = false;
            Refresh();
            if (StartWithScrolling)
            {
                _scrollingTask = StartScrollingAsync(null);
                _initialScrollingStarted = _scrolling;
                return;
            }

            Confirm();
        }
    }
}

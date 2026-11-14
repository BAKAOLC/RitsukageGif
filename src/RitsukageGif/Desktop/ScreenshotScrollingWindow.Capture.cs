namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class ScreenshotScrollingWindow
    {
        private void StartCapture()
        {
            _captureStarted = true;
            UpdateMoveControl();
            UpdateResizeTargets();
            ScrollingUiLogger.ZLogInformation($"Scrolling capture started. Region={_region} Axis={_axis.ToString()}");
            _session.Paused = false;
            _statusNoticeKey = null;
            SetStartStopContent();
            SetOutputActionsEnabled(_lastCaptureStatus is not null);
            RefreshStatus();
            RefreshLivePreview();
        }

        private async Task ToggleCaptureAsync()
        {
            if (_closed || _outputting || HasCaptureDialog || _resizePointer is not null) return;
            if (!_captureStarted)
            {
                StartCapture();
                return;
            }

            if (!_skipClearConfirmation)
            {
                var dialog = new ScreenshotScrollingClearDialog();
                _clearDialog = dialog;
                RefreshShortcutNativeState();
                bool confirmed;
                try
                {
                    confirmed = await dialog.ShowDialog<bool>(this);
                    if (dialog.DoNotAskAgain)
                    {
                        _skipClearConfirmation = true;
                        _clearConfirmationChanged?.Invoke(true);
                    }
                }
                finally
                {
                    _clearDialog = null;
                    RefreshShortcutNativeState();
                }

                if (!confirmed || _closed) return;
            }

            ResetCapture(_axis, 0, 0, false);
            ScrollingUiLogger.ZLogInformation($"Scrolling capture cleared. Region={_region} Axis={_axis.ToString()}");
        }
    }
}

using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class ScreenshotScrollingWindow
    {
        private Func<Window, ScrollCaptureImage, ScrollingCaptureAction, CancellationToken, Task<bool>>? _outputHandler;
        private ScrollingCaptureResult? _outputResult;
        private Task? _outputTask;
        private CancellationToken _outputToken;
        private bool _outputting;
        internal PixelPoint? CaptureOutputOrigin => _session.OutputOrigin;

        private Task CompleteCapture(ScrollingCaptureAction action)
        {
            if (_closed || _outputting || !_finish.IsEnabled) return Task.CompletedTask;
            return _outputTask = CompleteCaptureCoreAsync(action);
        }

        private async Task CompleteCaptureCoreAsync(ScrollingCaptureAction action)
        {
            _outputting = true;
            _cropPopup.IsOpen = false;
            _session.Paused = true;
            SetOutputActionsEnabled(false);
            RefreshShortcutNativeState();
            RefreshLivePreview();
            ScrollingUiLogger.ZLogInformation($"Scrolling output started. Action={action.ToString()}");
            try
            {
                var image = await Task.Run(() => _session.Export(_outputToken), _outputToken);
                _outputToken.ThrowIfCancellationRequested();
                if (_closed) return;
                var accepted = action == ScrollingCaptureAction.Edit || _outputHandler is null ||
                               await _outputHandler(this, image, action, _outputToken);
                _outputToken.ThrowIfCancellationRequested();
                if (_closed) return;
                ScrollingUiLogger.ZLogInformation(
                    $"Scrolling output completed. Action={action.ToString()} Accepted={accepted} Width={image.Width} Height={image.Height}");
                if (!accepted) return;
                _outputResult = new(image, action) { OutputOrigin = CaptureOutputOrigin };
                _completed.TrySetResult(true);
                Close();
            }
            catch (OperationCanceledException) when (_closed || _outputToken.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                ScrollingUiLogger.ZLogError(error, $"Scrolling output failed. Action={action.ToString()}");
                if (!_closed)
                {
                    _captureFailure = error.Message;
                    RefreshStatus();
                }
            }
            finally
            {
                _outputting = false;
                if (!_closed)
                {
                    SetOutputActionsEnabled(_lastCaptureStatus is not null);
                    RefreshShortcutNativeState();
                    SetPauseContent("ScrollCaptureResume", false);
                    RefreshStatus();
                    Activate();
                }
            }
        }
    }
}

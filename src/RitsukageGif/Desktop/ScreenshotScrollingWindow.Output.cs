using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class ScreenshotScrollingWindow
    {
        private const int FileOnlyWarningLength = 28000;
        private const int MaximumNonFileOutputLength = 29000;
        private Func<Window, ScrollCaptureImage, ScrollingCaptureAction, CancellationToken, Task<bool>>? _outputHandler;
        private ScrollingCaptureResult? _outputResult;
        private Task? _outputTask;
        private CancellationToken _outputToken;
        private bool _outputting;
        private bool _saveOnUserClose;
        internal PixelPoint? CaptureOutputOrigin => _session.OutputOrigin;
        internal bool IsFileOnlyCapture => _session.OutputAxisLength > MaximumNonFileOutputLength;

        private Task CloseByUserAsync()
        {
            if (_closed || _outputting) return Task.CompletedTask;
            if (_saveOnUserClose && !_outputToken.IsCancellationRequested && _lastCaptureStatus is not null &&
                IsOutputActionAvailable(ScrollingCaptureAction.Close, _session.OutputAxisLength))
                return CompleteCapture(ScrollingCaptureAction.Close);
            Close();
            return Task.CompletedTask;
        }

        private Task CompleteCapture(ScrollingCaptureAction action)
        {
            if (_closed || _outputting || _lastCaptureStatus is null ||
                !IsOutputActionAvailable(action, _session.OutputAxisLength)) return Task.CompletedTask;
            return _outputTask = CompleteCaptureCoreAsync(action);
        }

        private static bool IsOutputActionAvailable(ScrollingCaptureAction action, int length)
        {
            return length > 0 && (length <= MaximumNonFileOutputLength ||
                                  action is ScrollingCaptureAction.Save or ScrollingCaptureAction.QuickSave);
        }

        private async Task CompleteCaptureCoreAsync(ScrollingCaptureAction action)
        {
            var wasPaused = _session.Paused;
            var hideCaptureSurfaces = IsFileOnlyCapture &&
                                      action is ScrollingCaptureAction.Save or ScrollingCaptureAction.QuickSave;
            var originalOpacity = Opacity;
            var originalHitTest = IsHitTestVisible;
            _outputting = true;
            _cropPopup.IsOpen = false;
            _session.Paused = true;
            SetOutputActionsEnabled(false);
            RefreshShortcutNativeState();
            RefreshLivePreview();
            ScrollingUiLogger.ZLogInformation($"Scrolling output started. Action={action.ToString()}");
            try
            {
                if (!IsOutputActionAvailable(action, _session.OutputAxisLength)) return;
                if (hideCaptureSurfaces)
                {
                    Opacity = 0;
                    IsHitTestVisible = false;
                    foreach (var border in _borders) border.Hide();
                    foreach (var mask in _maskWindows) mask.Hide();
                }

                var image = await Task.Run(() => _session.ExportAsync(_outputToken), _outputToken);
                _outputToken.ThrowIfCancellationRequested();
                if (_closed) return;
                var accepted = _outputHandler is null ||
                               await _outputHandler(this, image, action, _outputToken);
                _outputToken.ThrowIfCancellationRequested();
                if (_closed) return;
                ScrollingUiLogger.ZLogInformation(
                    $"Scrolling output completed. Action={action.ToString()} Accepted={accepted} Width={image.Width} Height={image.Height}");
                if (!accepted) return;
                if (action != ScrollingCaptureAction.Close)
                    _outputResult = new(image, action) { OutputOrigin = CaptureOutputOrigin };
                _completed.TrySetResult(action != ScrollingCaptureAction.Close);
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
                    if (action == ScrollingCaptureAction.Close) Close();
                }
            }
            finally
            {
                _outputting = false;
                if (!_closed)
                {
                    if (hideCaptureSurfaces)
                    {
                        Opacity = originalOpacity;
                        IsHitTestVisible = originalHitTest;
                        UpdateBorders();
                    }

                    if (_outputResult is null && !_outputToken.IsCancellationRequested && _session.Paused != wasPaused)
                        _session.Paused = wasPaused;
                    SetOutputActionsEnabled(_lastCaptureStatus is not null);
                    RefreshShortcutNativeState();
                    SetStartStopContent();
                    RefreshStatus();
                    RefreshLivePreview();
                    Activate();
                }
            }
        }
    }
}

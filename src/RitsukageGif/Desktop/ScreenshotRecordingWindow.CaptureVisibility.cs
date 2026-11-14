using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotRecordingWindow
    {
        private string? _toolbarCaptureTitle;
        private (bool Excluded, LinuxCaptureExclusionLease? Lease)? _toolbarCaptureBeforeStart;

        private void ResetToolbarCaptureTitle()
        {
            if (_toolbarCaptureTitle is null) return;
            _toolbarCaptureTitle = null;
            Bind(TitleProperty, LiveText.Resource("RecorderTitle"));
        }

        private async Task ConfigureToolbarCaptureAsync(PixelRect bounds, CancellationToken token)
        {
            ResetToolbarCaptureTitle();
            var mode = _controller.ToolbarCaptureMode;
            var isolated = DesktopCapture.UsesSourceSelection
                ? _controller.SourceIsWindowIsolated
                : _controller.Selection.WindowSource || _controller.SourceIsWindowIsolated;
            var unknownCoordinates = DesktopCapture.UsesSourceSelection && !isolated;
            var overlap = !isolated && bounds.Intersect(_controller.Selection.Region).Height > bounds.Height * .5;
            var hide = mode == RecordingToolbarCaptureMode.Hide;
            if (mode == RecordingToolbarCaptureMode.Ask && (overlap || unknownCoordinates))
            {
                var dialog = new RecordingToolbarCaptureDialog(!unknownCoordinates);
                await using var registration = token.Register(() => Dispatcher.UIThread.Post(dialog.Close));
                hide = await dialog.ShowDialog<bool>(this);
                token.ThrowIfCancellationRequested();
                if (dialog.RememberChoice)
                    _controller.ConfigureToolbarCaptureMode(hide
                        ? RecordingToolbarCaptureMode.Hide
                        : RecordingToolbarCaptureMode.Show);
            }

            _toolbarCaptureBeforeStart = (_captureExcluded, _recorderExclusion);
            _controller.ConfigureIncludedCaptureWindowTitle(null);
            if (!hide)
            {
                if (OperatingSystem.IsWindows() && _captureExcluded && !RecordingRegionOverlay.Exclude(this, false))
                    throw new IOException(UiText.Get("RecordingToolbarCaptureUnavailable"));
                if (_recorderExclusion is { } lease)
                {
                    _recorderExclusion = null;
                    await lease.DisposeAsync();
                    if (lease.ReleaseFailure != LinuxCaptureExclusionFailure.None)
                        throw new IOException(UiText.Get("RecordingToolbarCaptureUnavailable"));
                }

                _captureExcluded = false;
                if (OperatingSystem.IsMacOS() && !isolated)
                {
                    _toolbarCaptureTitle = "RitsukageGif.RecordingToolbar." + Guid.NewGuid().ToString("N");
                    Title = _toolbarCaptureTitle;
                    _controller.ConfigureIncludedCaptureWindowTitle(_toolbarCaptureTitle);
                }

                _captureHidden = false;
            }
            else
            {
                if (OperatingSystem.IsWindows() && !_captureExcluded)
                    _captureExcluded = RecordingRegionOverlay.Exclude(this);
                if (_captureProof is { Source: LinuxCaptureExclusionSource.KWinPortal } proof)
                    await PrepareRecorderExclusionAsync(proof, token);
                _captureHidden = !_captureExcluded && !SourceExcludesRecorder &&
                                 (unknownCoordinates || !bounds.Intersect(_controller.Selection.Region).IsEmpty);
                if (_captureHidden) await CaptureWindowVisibility.HideAsync(this, token);
            }

            LogHub.GetLogger("Desktop.ScreenshotRecording").ZLogInformation(
                $"Recording toolbar capture configured. HiddenFromCapture={hide} WindowExcluded={_captureExcluded} ControlHidden={_captureHidden}");
        }

        private async Task CompleteToolbarCapturePreparationAsync(bool restore, CancellationToken token)
        {
            var previous = _toolbarCaptureBeforeStart;
            _toolbarCaptureBeforeStart = null;
            if (!restore || previous is not { } policy) return;
            if (OperatingSystem.IsWindows() && _captureExcluded != policy.Excluded)
            {
                if (!RecordingRegionOverlay.Exclude(this, policy.Excluded))
                    throw new IOException(UiText.Get("RecordingToolbarCaptureUnavailable"));
                _captureExcluded = policy.Excluded;
            }

            if (!ReferenceEquals(policy.Lease, _recorderExclusion))
            {
                if (_recorderExclusion is { } lease)
                {
                    _recorderExclusion = null;
                    _captureExcluded = false;
                    await lease.DisposeAsync();
                    if (lease.ReleaseFailure != LinuxCaptureExclusionFailure.None)
                        throw new IOException(UiText.Get("RecordingToolbarCaptureUnavailable"));
                }

                if (policy.Lease is not null && _captureProof is { } proof)
                {
                    await PrepareRecorderExclusionAsync(proof, token);
                    if (!_captureExcluded)
                        throw new IOException(UiText.Get("RecordingToolbarCaptureUnavailable"));
                }
            }

            ResetToolbarCaptureTitle();
            _controller.ConfigureIncludedCaptureWindowTitle(null);
        }
    }
}

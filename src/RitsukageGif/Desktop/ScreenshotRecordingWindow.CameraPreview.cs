namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotRecordingWindow
    {
        private RecordingCameraPreviewOverlay? _cameraPreview;
        private event Action? CameraOptionsChanged;

        private async Task ChangeCameraPreviewAsync(RecordingCameraSession? session)
        {
            var previous = _cameraPreview;
            _cameraPreview = null;
            if (previous is not null)
            {
                previous.Failed -= CameraPreviewFailed;
                await previous.DisposeAsync();
            }

            if (_sourceViewport is { } viewport)
            {
                await viewport.SetCameraAsync(session);
                return;
            }

            if (session is null || _closed) return;
            var preview = await RecordingCameraPreviewOverlay.AttachAsync(this, session, _controller.Selection.Region,
                CameraPreviewOptionsChanged, SourceExcludesRecorder, _captureProof);
            preview.Failed += CameraPreviewFailed;
            _cameraPreview = preview;
        }

        private void CameraPreviewOptionsChanged(RecordingCameraOptions options)
        {
            _controller.UpdateCameraPreviewOptions(options);
            CameraOptionsChanged?.Invoke();
        }

        private void UpdateCameraPreview(bool visible)
        {
            if (_sourceViewport is { } viewport)
            {
                viewport.SetPreviewVisible(visible && _controller is { IsClosed: false, Reviewing: false });
                return;
            }

            try
            {
                _cameraPreview?.UpdateGeometry(_controller.Selection.Region, visible && !_controller.IsPreparingSource,
                    SourceExcludesRecorder);
            }
            catch (NotSupportedException error)
            {
                CameraPreviewFailed(error.Message);
            }
        }

        private void CameraPreviewFailed(string error)
        {
            var preview = _cameraPreview;
            _cameraPreview = null;
            if (preview is not null)
            {
                preview.Failed -= CameraPreviewFailed;
                _ = GuardAsync(async () => await preview.DisposeAsync());
            }

            _controller.ReportCameraPreviewFailure(error);
        }
    }
}

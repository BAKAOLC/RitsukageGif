namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotRecordingWindow
    {
        private LinuxCaptureExclusionCapability _nativeAnnotationCapability;
        private long _nativeExclusionSourceRevision = -1;
        private bool _preparingNativeExclusion;
        private LinuxCaptureExclusionProof? _captureProof;
        private LinuxCaptureExclusionLease? _recorderExclusion;
        private ScreenshotRecordingSourceViewport? _sourceViewport;

        private async Task ReleaseRecorderExclusionAsync()
        {
            _nativeAnnotationCapability = LinuxCaptureExclusionCapability.Unavailable;
            _captureProof = null;
            await ChangeCameraPreviewAsync(null);
            var viewport = _sourceViewport;
            _sourceViewport = null;
            if (viewport is not null) await viewport.DisposeAsync();
            try
            {
                await Annotations.ReleaseNativeExclusionAsync();
            }
            finally
            {
                var recorderExclusion = _recorderExclusion;
                _recorderExclusion = null;
                if (recorderExclusion is not null)
                {
                    Hide();
                    _captureExcluded = false;
                    await recorderExclusion.DisposeAsync();
                }
            }
        }

        private void NativeAnnotationExclusionFailed(LinuxCaptureExclusionResult result)
        {
            _nativeAnnotationCapability = LinuxCaptureExclusionCapability.Unavailable;
            LogHub.GetLogger("Desktop.ScreenshotRecording").ZLogWarning(
                $"Live recording annotation exclusion was lost. Detail={result.Failure}");
            if (!_closed && !_preparingNativeExclusion)
                UpdateAnnotations(IsVisible && _controller is { IsClosed: false, Reviewing: false });
        }

        private async Task PrepareNativeSourceExclusionAsync(LinuxCaptureExclusionProof proof, CancellationToken token)
        {
            _nativeAnnotationCapability = LinuxCaptureExclusionCapability.Unavailable;
            _preparingNativeExclusion = true;
            try
            {
                await ChangeCameraPreviewAsync(null);
                LinuxCaptureExclusionResult result;
                if (_controller.Selection.CaptureSession is { } source)
                {
                    var previous = _sourceViewport;
                    _sourceViewport = null;
                    if (previous is not null) await previous.DisposeAsync();
                    var viewport = new ScreenshotRecordingSourceViewport(source, Annotations,
                        _controller.Selection.Region);
                    viewport.PlacementChanged += Reposition;
                    viewport.CloseRequested += CancelAnnotationContext;
                    viewport.Failed += _controller.ReportSourcePreviewFailure;
                    viewport.CameraOptionsChanged += CameraPreviewOptionsChanged;
                    _sourceViewport = viewport;
                    UpdateAnnotations(IsVisible);
                    result = await viewport.ShowSourceAsync(proof, token);
                    if (result.Capability == LinuxCaptureExclusionCapability.Unavailable)
                    {
                        _sourceViewport = null;
                        await viewport.DisposeAsync();
                    }
                    else
                    {
                        await Annotations.BindHostedSourceExclusionAsync(proof, token);
                    }
                }
                else
                {
                    UpdateAnnotations(IsVisible);
                    result = await Annotations.PrepareNativeExclusionAsync(proof, token);
                }

                _nativeAnnotationCapability = result.Capability;
                _captureProof = proof;
                if (proof.Source == LinuxCaptureExclusionSource.KWinPortal)
                    await PrepareRecorderExclusionAsync(proof, token);
                if (_controller is { CameraSession: { } camera, IsClosed: false })
                    await ChangeCameraPreviewAsync(camera);
                if (result.Capability == LinuxCaptureExclusionCapability.Unavailable &&
                    result.Failure != LinuxCaptureExclusionFailure.None)
                    LogHub.GetLogger("Desktop.ScreenshotRecording").ZLogWarning(
                        $"Live recording annotation exclusion is unavailable. Detail={result.Failure}");
            }
            finally
            {
                _preparingNativeExclusion = false;
                if (!_closed) UpdateAnnotations(IsVisible && _controller is { IsClosed: false, Reviewing: false });
            }
        }

        private async Task PrepareRecorderExclusionAsync(LinuxCaptureExclusionProof proof, CancellationToken token)
        {
            if (_recorderExclusion is not null) return;
            var title = Title;
            var marker = LinuxCaptureExclusionLease.CreateWindowMarker();
            Opacity = 0;
            SetCurrentValue(TitleProperty, marker);
            try
            {
                var result = await LinuxCaptureExclusionLease.CreateAsync(proof, marker, token);
                _recorderExclusion = result.Lease;
                _captureExcluded = result.Capability == LinuxCaptureExclusionCapability.NativeCompositorExclusion;
                if (!_captureExcluded)
                    LogHub.GetLogger("Desktop.ScreenshotRecording").ZLogWarning(
                        $"Recording control exclusion is unavailable. Detail={result.Failure}");
            }
            finally
            {
                SetCurrentValue(TitleProperty, title);
                Opacity = 1;
            }
        }
    }
}

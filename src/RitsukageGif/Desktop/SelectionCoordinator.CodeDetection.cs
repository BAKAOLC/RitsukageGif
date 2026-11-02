using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SelectionCoordinator
    {
        private ScreenshotQrDetectionSession? _codeDetection;
        private bool? _codeDetectionEnabled;
        private (PixelRect Region, byte[] Pixels)[] _codeFrames = [];

        internal string? CompletionNotice { get; private set; }

        internal QrDetectionSnapshot? DetectedCodes => _codeDetection?.Snapshot;

        internal bool CodeDetectionEnabled => _codeDetectionEnabled ?? CapturePreferences.CodeDetection is
            CaptureCodeDetectionMode.Automatic or CaptureCodeDetectionMode.Contents;

        internal bool ShowDetectedCodeContents => CapturePreferences.CodeDetection == CaptureCodeDetectionMode.Contents;

        internal bool CodeOverlayVisible => CodeDetectionEnabled && !_scrolling && !SelectionManipulating &&
                                            Snapshot.Phase == SelectionPhase.Selected && (!WindowSource ||
                                                UsesLocalSource || _frozenWindow is not null);

        internal bool CanShowCodeDetectionToggle =>
            CapturePreferences.CodeDetection != CaptureCodeDetectionMode.Disabled &&
            DetectedCodes is { } detection &&
            detection.Codes.Any(code => CodeCenterInSelection(code, detection.Region));

        internal void CompleteCodeWithoutResult()
        {
            if (_finished) return;
            CompletionNotice = UiText.Get("BarcodeRecognitionEmpty");
            RequestFinish(null);
        }

        internal void SetCodeDetectionEnabled(bool enabled)
        {
            if (!ScreenshotMode || _scrolling ||
                CapturePreferences.CodeDetection == CaptureCodeDetectionMode.Disabled) return;
            _codeDetectionEnabled = enabled;
            Refresh();
        }

        private bool CodeCenterInSelection(BarcodeRecognitionResult code, PixelRect source)
        {
            var points = code.Outline.Count > 0 ? code.Outline : code.Points;
            return points.Count > 0 && Snapshot.Region.Contains(new(source.X + (int)points.Average(point => point.X),
                source.Y + (int)points.Average(point => point.Y)));
        }

        private void UpdateCodeDetection()
        {
            var snapshot = Snapshot;
            if (!ScreenshotMode || _scrolling ||
                CapturePreferences.CodeDetection == CaptureCodeDetectionMode.Disabled ||
                snapshot.ShapeMode != SelectionShapeMode.Rectangle ||
                (WindowSource && !UsesLocalSource && _frozenWindow is null))
            {
                _codeDetection?.Clear();
                return;
            }

            if (_screenshotCompleting || SelectionManipulating || snapshot.Phase is not SelectionPhase.Selected ||
                snapshot.Region.IsEmpty) return;
            var sourceChanged = UpdateCodeSourceFrames();
            if (_codeFrames.Length == 0)
            {
                _codeDetection?.Clear();
                return;
            }

            if (_codeDetection is null)
            {
                _codeDetection = new(action => Dispatcher.UIThread.Post(action),
                    error => SelectionLogger.ZLogError(error,
                        $"Snapshot code detection failed. SelectionId={_selectionId}"));
                _codeDetection.Changed += Refresh;
            }

            if (!sourceChanged && _codeDetection.RequestedRegion.Intersect(snapshot.Region) == snapshot.Region) return;
            var frames = _codeFrames;
            var region = snapshot.Region;
            SelectionLogger.ZLogDebug(
                $"Snapshot code scan requested. SelectionId={_selectionId} Region={region} Mode={CapturePreferences.CodeDetection.ToString()}");
            _codeDetection.Update(frames, region,
                token => ScreenshotCodeScanner.ScanAsync(region, frames, token), _cancel.Token);
        }

        private bool UpdateCodeSourceFrames()
        {
            if (WindowSource && !UsesLocalSource)
            {
                if (_frozenWindow is not { } frame) return false;
                if (_codeFrames.Length == 1 && _codeFrames[0].Region == frame.Region &&
                    ReferenceEquals(_codeFrames[0].Pixels, frame.Pixels)) return false;
                _codeFrames = [(frame.Region, frame.Pixels)];
                return true;
            }

            var same = _codeFrames.Length == _screenshots.Count;
            for (var index = 0; same && index < _screenshots.Count; index++)
                same = _screenshots[index].Region == _codeFrames[index].Region &&
                       ReferenceEquals(_screenshots[index].Pixels, _codeFrames[index].Pixels);
            if (same) return false;
            _codeFrames = [.. _screenshots];
            return true;
        }

        private void DisposeCodeDetection()
        {
            _codeDetection?.Dispose();
            _codeFrames = [];
        }
    }
}

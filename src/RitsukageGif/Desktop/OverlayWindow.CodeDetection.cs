using Avalonia.Input.Platform;

namespace RitsukageGif.Desktop
{
    internal sealed partial class OverlayWindow
    {
        private static readonly ILogger CodeActionLogger = LogHub.GetLogger("Desktop.Selection.CodeDetection");
        private ScreenshotQrOverlay? _codeOverlay;

        private void InitializeCodeOverlay()
        {
            if (!_coordinator.ScreenshotMode) return;
            _codeOverlay = new(async text =>
            {
                var clipboard = Clipboard ?? throw new InvalidOperationException(UiText.Get("ClipboardFailed"));
                await clipboard.SetTextAsync(text);
                await clipboard.FlushAsync();
            }, async uri =>
            {
                if (!await Launcher.LaunchUriAsync(uri))
                    throw new InvalidOperationException(UiText.Get("QrCodeOpenLinkFailed"));
            }, error => CodeActionLogger.ZLogError(error, $"Detected code action failed"));
            _codeOverlay.LayoutChanged += () =>
            {
                _canvas.CodeReservedBounds = _codeOverlay.OccupiedBounds;
                _canvas.InvalidateVisual();
            };
            _layers.Children.Insert(_annotationSurface is null ? 1 : _layers.Children.IndexOf(_annotationSurface) + 1,
                _codeOverlay);
        }

        private void UpdateCodeOverlay(PixelRect selected)
        {
            _codeOverlay?.Update(_coordinator.DetectedCodes, selected, Region, _canvas.Scale,
                _coordinator.CodeOverlayVisible,
                _coordinator.ShowDetectedCodeContents,
                _coordinator is { SelectionManipulating: false, Annotations: not { IsDrawing: true } },
                [
                    _canvas.ReservedBounds, _canvas.SecondaryReservedBounds, _canvas.FrameReservedBounds,
                    _canvas.ModePickerReservedBounds, _canvas.InformationReservedBounds,
                ]);
        }
    }
}

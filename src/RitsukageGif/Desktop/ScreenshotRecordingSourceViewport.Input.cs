using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotRecordingSourceViewport
    {
        private RecordingCameraSlot? _dragCamera;
        private int _dragCameraIndex;
        private Point _dragCameraOrigin;
        private bool _panCamera;
        private RecordingCameraOptions? _notifiedCameraOptions;

        private void InitializeCameraInput()
        {
            _content.Background = Brushes.Transparent;
            _content.AddHandler(PointerPressedEvent, (_, args) =>
            {
                if (_annotations.Session.Tool != ScreenshotTool.Select || _camera is not { } camera ||
                    !args.GetCurrentPoint(_content).Properties.IsLeftButtonPressed) return;
                var point = args.GetPosition(_content);
                using var snapshot = camera.CaptureSnapshot(_region.Width, _region.Height);
                for (var slot = snapshot.SlotBounds.Count - 1; slot >= 0; slot--)
                {
                    if (!snapshot.SlotBounds[slot].Contains((float)point.X, (float)point.Y)) continue;
                    _dragCameraIndex = slot;
                    var options = camera.Options;
                    var selected = slot == 0 ? options.Primary : options.Secondary;
                    var bounds = snapshot.SlotBounds[slot];
                    _dragCamera = selected with
                    {
                        Position = selected.Position ?? new PixelPoint((int)bounds.Left, (int)bounds.Top),
                    };
                    _dragCameraOrigin = point;
                    _panCamera = args.KeyModifiers.HasFlag(KeyModifiers.Shift);
                    args.Pointer.Capture(_content);
                    args.Handled = true;
                    break;
                }
            }, RoutingStrategies.Tunnel);
            _content.AddHandler(PointerMovedEvent, (_, args) =>
            {
                if (_dragCamera is not { } start || _camera is not { } camera) return;
                var delta = args.GetPosition(_content) - _dragCameraOrigin;
                var options = camera.Options;
                var current = _dragCameraIndex == 0 ? options.Primary : options.Secondary;
                var slot = _panCamera
                    ? current with
                    {
                        PanOffset = new(start.PanOffset.X - (float)delta.X, start.PanOffset.Y - (float)delta.Y),
                    }
                    : current with
                    {
                        Position = new PixelPoint(start.Position!.Value.X + (int)Math.Round(delta.X),
                            start.Position.Value.Y + (int)Math.Round(delta.Y)),
                    };
                camera.ApplyOptions(_dragCameraIndex == 0
                    ? options with { Primary = slot }
                    : options with { Secondary = slot });
                NotifyCameraOptions();
                QueueFrame();
                args.Handled = true;
            }, RoutingStrategies.Tunnel);
            _content.PointerReleased += (_, args) =>
            {
                if (_dragCamera is null) return;
                _dragCamera = null;
                args.Pointer.Capture(null);
                args.Handled = true;
            };
            _content.PointerCaptureLost += (_, _) => _dragCamera = null;
        }

        private void NotifyCameraOptions()
        {
            if (_camera is not { } camera || camera.Options == _notifiedCameraOptions) return;
            _notifiedCameraOptions = camera.Options;
            CameraOptionsChanged?.Invoke(camera.Options);
        }
    }
}

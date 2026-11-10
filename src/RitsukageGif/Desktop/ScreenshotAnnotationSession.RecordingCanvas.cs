using System.Runtime.InteropServices;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        private WriteableBitmap? _recordingEraserMask;

        private void DrawRecordingEraser(DrawingContext context, ScreenshotAnnotation annotation)
        {
            if (_recordingEraserMask is null)
            {
                _recordingEraserMask = new(new(1, 1), new(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
                using var buffer = _recordingEraserMask.Lock();
                Marshal.WriteInt32(buffer.Address, -1);
            }

            using var clip = context.PushGeometryClip(PixelGeometryFor(annotation));
            using var options = context.PushRenderOptions(new()
            {
                BitmapBlendingMode = BitmapBlendingMode.DestinationOut,
                BitmapInterpolationMode = BitmapInterpolationMode.None,
            });
            context.DrawImage(_recordingEraserMask, PixelEffectBounds(annotation));
        }

        private void DisposeRecordingCanvas()
        {
            _recordingEraserMask?.Dispose();
            _recordingEraserMask = null;
        }
    }
}
